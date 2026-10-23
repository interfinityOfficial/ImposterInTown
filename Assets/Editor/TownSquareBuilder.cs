using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// Builds a low-poly town square from the Kenney Fantasy Town Kit + Mini Characters.
// Menu: Tools > Imposter In Town > Build Town Square. Re-running regenerates Assets/Scenes/TownSquare.unity,
// keeping whatever you've tuned in the Inspector on the Imposter Game and the camera (see KeepTuning).
public static class TownSquareBuilder
{
    const string TownDir = "Assets/Kenney/FantasyTown";
    const string CharDir = "Assets/Kenney/MiniCharacters";
    const string GenDir = "Assets/Kenney/Generated";
    const string ScenePath = "Assets/Scenes/TownSquare.unity";
    const string SkateDir = "Assets/Kenney/MiniSkate";
    const string PlayerModel = SkateDir + "/character-skate-boy.fbx";
    const int VillagersPerType = 2;   // every outfit appears twice, so the imposter always has an innocent twin
    // 10 outfits x 2 = 20 villagers.
    static readonly string[] ExcludedVillagers = { CharDir + "/character-male-c.fbx", CharDir + "/character-female-c.fbx" };
    static readonly Vector3 PlayerStart = new Vector3(0f, 0f, -4f); // close enough that the fountain sits near mid-screen

    const int PlazaHalf = 9;     // plaza spans -9..9 on X and Z (1 unit = 1 kit tile)
    const int HouseDepth = 2;
    const int RoadEnd = 60;           // streets run out to the edge of the grass so the camera never sees them stop
    const float OutskirtsEnd = 50f;   // trees and rocks fill the countryside out to here
    const int Seed = 1234;

    static Material townMat;
    static Material charMat;
    static Material playerMat;
    static Material waterMat;
    static System.Random rng;

    [MenuItem("Tools/Imposter In Town/Build Town Square")]
    public static void Build()
    {
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        rng = new System.Random(Seed);
        Directory.CreateDirectory(GenDir);
        ConfigureImports();
        CreateMaterials();
        CaptureTuning();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var env = new GameObject("Environment").transform;
        BuildGround(env);
        BuildPlaza(env);
        BuildHouses(env);
        BuildMarket(env);
        BuildOutskirts(env);
        AddColliders(env);
        BuildActivitySpots();
        BakeNavMesh(env);
        BuildVillagers();
        var player = BuildPlayer();
        BuildLightingAndCamera(player);
        BuildImposterGame(player);

        EditorSceneManager.SaveScene(scene, ScenePath);

        // Point the Scene view down the game camera so editing starts from the player's view.
        var sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null && Camera.main != null) sceneView.AlignViewToObject(Camera.main.transform);
        AddSceneToBuild();
        Debug.Log("[TownSquareBuilder] Built " + ScenePath);
    }

    // Entry point for: Unity -batchmode -executeMethod TownSquareBuilder.BuildAndCapture
    public static void BuildAndCapture()
    {
        Build();
        var cam = Camera.main;
        var orbit = cam.GetComponent<OrbitCamera>();
        Capture(cam, "Screenshots/town_player.png"); // first render warms up shaders/shadows
        Capture(cam, "Screenshots/town_player.png");

        var follow = orbit.follow;
        orbit.follow = null; orbit.targetScreenY = 0.5f; orbit.yaw = 25f; orbit.pitch = 42f; orbit.distance = 22f; orbit.Apply();
        Capture(cam, "Screenshots/town_overview.png");
        orbit.follow = follow; orbit.targetScreenY = 0.33f; orbit.yaw = 0f; orbit.pitch = 35f; orbit.distance = 8.5f; orbit.Apply();
    }

    // ---------- Import setup ----------

    static void ConfigureImports()
    {
        foreach (var texPath in new[] { TownDir + "/Textures/colormap.png", CharDir + "/Textures/colormap.png", SkateDir + "/Textures/colormap.png" })
        {
            var ti = (TextureImporter)AssetImporter.GetAtPath(texPath);
            if (ti != null && ti.filterMode != FilterMode.Point)
            {
                ti.filterMode = FilterMode.Point;
                ti.mipmapEnabled = false;
                ti.SaveAndReimport();
            }
        }

        // Clips that repeat while a villager holds the action (VillagerAI decides how long).
        var looping = new HashSet<string> { "idle", "walk", "sprint", "crouch", "sit", "drive",
            "holding-right", "holding-left", "holding-both", "emote-yes", "emote-no", "interact-left", "interact-right" };
        foreach (var path in CharacterPaths().Append(PlayerModel))
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(path);
            bool upToDate = mi.animationType == ModelImporterAnimationType.Generic && mi.clipAnimations.Length > 0 &&
                            mi.clipAnimations.All(c => c.loopTime == looping.Contains(c.name));
            if (upToDate) continue;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.clipAnimations = mi.defaultClipAnimations.Select(c => { c.loopTime = looping.Contains(c.name); return c; }).ToArray();
            mi.SaveAndReimport();
        }
    }

    static IEnumerable<string> CharacterPaths() =>
        Directory.GetFiles(CharDir, "character-*.fbx").Select(p => p.Replace('\\', '/')).OrderBy(p => p);

    static void CreateMaterials()
    {
        townMat = MakeMaterial("TownColormap", TownDir + "/Textures/colormap.png");
        charMat = MakeMaterial("CharacterColormap", CharDir + "/Textures/colormap.png");
        playerMat = MakeMaterial("SkaterColormap", SkateDir + "/Textures/colormap.png"); // the skate pack has its own palette
        waterMat = ColorMaterial("Water", new Color(0.45f, 0.75f, 0.92f));
        waterMat.SetFloat("_Smoothness", 0.85f);
    }

    static Material MakeMaterial(string name, string texPath)
    {
        string path = $"{GenDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", 0.1f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    static Material ColorMaterial(string name, Color color, bool unlit = false)
    {
        string path = $"{GenDir}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find(unlit ? "Universal Render Pipeline/Unlit" : "Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", color);
        if (!unlit) mat.SetFloat("_Smoothness", 0.05f);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // ---------- Placement helpers ----------

    static GameObject Place(string piece, Transform parent, Vector3 localPos, float yaw = 0f, float scale = 1f)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{TownDir}/{piece}.fbx");
        if (prefab == null) { Debug.LogError("Missing piece " + piece); return null; }
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one * scale;
        foreach (var r in go.GetComponentsInChildren<Renderer>())
        {
            // Swap the kit's imported materials for ours; the fountain's tiled "Water" slot gets a flat water colour.
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
                mats[i] = mats[i] != null && mats[i].name.ToLower().Contains("water") ? waterMat : townMat;
            r.sharedMaterials = mats;
        }
        return go;
    }

    // Kit walls/banners/awnings sit on one edge of their 1x1 tile. Rather than hard-coding which
    // edge (depends on FBX axis conversion), try the 4 rotations and keep the one whose bounds
    // centre points toward `outward` (in the parent's local space).
    static GameObject PlaceEdge(string piece, Transform parent, Vector3 cellCenter, Vector3 outward)
    {
        var go = Place(piece, parent, cellCenter);
        if (go == null) return null;
        float best = float.NegativeInfinity; int bestRot = 0;
        for (int r = 0; r < 4; r++)
        {
            go.transform.localRotation = Quaternion.Euler(0f, r * 90f, 0f);
            Vector3 offset = parent.InverseTransformPoint(Bounds(go).center) - cellCenter;
            offset.y = 0f;
            float score = Vector3.Dot(offset, outward);
            if (score > best) { best = score; bestRot = r; }
        }
        go.transform.localRotation = Quaternion.Euler(0f, bestRot * 90f, 0f);
        return go;
    }

    static Bounds Bounds(GameObject go)
    {
        var rs = go.GetComponentsInChildren<Renderer>();
        var b = rs[0].bounds;
        foreach (var r in rs) b.Encapsulate(r.bounds);
        return b;
    }

    static T Pick<T>(params T[] options) => options[rng.Next(options.Length)];
    static float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

    // ---------- Scene parts ----------

    static void BuildGround(Transform env)
    {
        var grass = GameObject.CreatePrimitive(PrimitiveType.Plane);
        grass.name = "Grass";
        grass.transform.SetParent(env);
        grass.transform.localPosition = new Vector3(0f, -0.02f, 0f);
        grass.transform.localScale = new Vector3(12f, 1f, 12f);
        grass.GetComponent<Renderer>().sharedMaterial = ColorMaterial("Grass", new Color(0.47f, 0.68f, 0.38f));
    }

    static void BuildPlaza(Transform env)
    {
        var plaza = new GameObject("Plaza").transform;
        plaza.SetParent(env);

        // Paving: the square itself plus a street leaving through the middle of each side.
        var cells = new HashSet<Vector2Int>(); // each paved tile, by its min corner
        for (int x = -PlazaHalf; x < PlazaHalf; x++)
            for (int z = -PlazaHalf; z < PlazaHalf; z++)
                if (new Vector2(x + 0.5f, z + 0.5f).magnitude > 1.2f) // leave the fountain basin unpaved (avoids z-fighting with the water)
                    cells.Add(new Vector2Int(x, z));
        for (int d = PlazaHalf; d < RoadEnd; d++)
            for (int t = -1; t < 1; t++)
            {
                cells.Add(new Vector2Int(t, d));
                cells.Add(new Vector2Int(t, -d - 1));
                cells.Add(new Vector2Int(d, t));
                cells.Add(new Vector2Int(-d - 1, t));
            }
        BuildPaving(plaza, cells);

        // Fountain centrepiece.
        Place("fountain-round-detail", plaza, new Vector3(0f, 0.02f, 0f), 0f, 1.6f).name = "Fountain";

        // Benches ringing the fountain, long side tangent to the circle.
        for (int i = 0; i < 4; i++)
        {
            float a = (45f + i * 90f) * Mathf.Deg2Rad;
            var pos = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 3.3f;
            var tangent = new Vector3(-Mathf.Sin(a), 0f, Mathf.Cos(a));
            Place("stall-bench", plaza, pos, Mathf.Atan2(tangent.x, tangent.z) * Mathf.Rad2Deg, 1.3f);
        }

        // Lanterns on the diagonals and flanking each street entrance.
        for (int i = 0; i < 4; i++)
        {
            float a = (45f + i * 90f) * Mathf.Deg2Rad;
            Place("lantern", plaza, new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 5.2f);
        }
        float e = PlazaHalf - 0.6f;
        foreach (var p in new[] {
            new Vector3(-1.6f, 0, e), new Vector3(1.6f, 0, e), new Vector3(-1.6f, 0, -e), new Vector3(1.6f, 0, -e),
            new Vector3(e, 0, -1.6f), new Vector3(e, 0, 1.6f), new Vector3(-e, 0, -1.6f), new Vector3(-e, 0, 1.6f) })
            Place("lantern", plaza, p);

        // Trees in the plaza corners.
        foreach (var c in new[] { new Vector3(-7.6f, 0, -7.6f), new Vector3(7.6f, 0, -7.6f), new Vector3(-7.6f, 0, 7.6f), new Vector3(7.6f, 0, 7.6f) })
            Place("tree-high-round", plaza, c, Range(0, 360));
    }

    // The kit's road tile is a thin slab, and hundreds of them side by side show a line at every joint, worst
    // under low side light (each slab's edges catch the light and cast a sliver of shadow). Instead the paving is
    // a few meshes, each a single top surface sharing vertices across its tiles, with an edge only around the
    // outside of the paving. Same height and colour as the kit's tile.
    // It's cut into PavingChunk-sized squares rather than one mesh: the web build lights each object with at
    // most 4 nearby lamps, and the lanterns' pools of light on the ground need each patch to pick its own.
    // Neighbouring chunks share their edge vertices exactly, so there's no line between them.
    const int PavingChunk = 3;

    static void BuildPaving(Transform plaza, HashSet<Vector2Int> cells)
    {
        var sample = Place("road", plaza, Vector3.zero);
        float top = plaza.InverseTransformPoint(Bounds(sample).max).y;
        Vector2 uv = sample.GetComponentInChildren<MeshFilter>().sharedMesh.uv[0]; // the whole tile is one colour
        Object.DestroyImmediate(sample);

        var root = new GameObject("Paving").transform;
        root.SetParent(plaza, false);
        string path = $"{GenDir}/Paving.asset";
        AssetDatabase.DeleteAsset(path);

        Vector2Int ChunkOf(Vector2Int c) => new Vector2Int(Mathf.FloorToInt((float)c.x / PavingChunk), Mathf.FloorToInt((float)c.y / PavingChunk));
        bool first = true;
        foreach (var chunk in cells.GroupBy(ChunkOf).OrderBy(g => g.Key.x).ThenBy(g => g.Key.y))
        {
            var mesh = PavingMesh(chunk, cells, top, uv);
            mesh.name = $"Paving {chunk.Key.x},{chunk.Key.y}";
            if (first) AssetDatabase.CreateAsset(mesh, path);
            else AssetDatabase.AddObjectToAsset(mesh, path);
            first = false;

            var part = new GameObject(mesh.name);
            part.transform.SetParent(root, false);
            part.AddComponent<MeshFilter>().sharedMesh = mesh;
            part.AddComponent<MeshRenderer>().sharedMaterial = townMat;
            GameObjectUtility.SetStaticEditorFlags(part, StaticEditorFlags.BatchingStatic);
        }
        AssetDatabase.SaveAssets();
    }

    // The tiles in `chunk` as one surface; `all` is every paved tile, so edges are only made at the paving's outside.
    static Mesh PavingMesh(IEnumerable<Vector2Int> chunk, HashSet<Vector2Int> all, float top, Vector2 uv)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var tris = new List<int>();
        var corners = new Dictionary<Vector2Int, int>();
        int Corner(Vector2Int p)
        {
            if (corners.TryGetValue(p, out int i)) return i;
            corners[p] = verts.Count;
            verts.Add(new Vector3(p.x, top, p.y));
            normals.Add(Vector3.up);
            return verts.Count - 1;
        }
        void Quad(int a, int b, int c, int d) => tris.AddRange(new[] { a, b, c, a, c, d }); // clockwise seen from the front

        foreach (var cell in chunk)
        {
            Quad(Corner(cell), Corner(cell + Vector2Int.up), Corner(cell + Vector2Int.one), Corner(cell + Vector2Int.right));

            // An edge face down to the ground wherever the next tile isn't paved.
            foreach (var dir in new[] { Vector2Int.up, Vector2Int.right, Vector2Int.down, Vector2Int.left })
            {
                if (all.Contains(cell + dir)) continue;
                // The tile's side facing `dir`, from p0 to p1 going clockwise around the tile seen from above.
                var side = new Vector2Int(dir.y, -dir.x);
                Vector2 mid = cell + new Vector2(0.5f, 0.5f) + (Vector2)dir * 0.5f;
                Vector2 p0 = mid - (Vector2)side * 0.5f, p1 = mid + (Vector2)side * 0.5f;
                var n = new Vector3(dir.x, 0f, dir.y);
                int s = verts.Count;
                verts.AddRange(new[] { new Vector3(p0.x, top, p0.y), new Vector3(p1.x, top, p1.y), new Vector3(p1.x, 0f, p1.y), new Vector3(p0.x, 0f, p0.y) });
                normals.AddRange(new[] { n, n, n, n });
                Quad(s + 1, s, s + 3, s + 2);
            }
        }

        var mesh = new Mesh();
        mesh.SetVertices(verts);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, Enumerable.Repeat(uv, verts.Count).ToList());
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateBounds();
        return mesh;
    }

    static void BuildHouses(Transform env)
    {
        var root = new GameObject("Houses").transform;
        root.SetParent(env);

        // Each side: outward normal, plus house widths for the two halves either side of the street.
        var sides = new[] {
            (outward: Vector3.forward, left: new[] { 3, 2, 3 }, right: new[] { 2, 3, 3 }, maxFloors: 3),
            (outward: Vector3.right,   left: new[] { 2, 3, 3 }, right: new[] { 3, 3, 2 }, maxFloors: 3),
            (outward: Vector3.back,    left: new[] { 3, 3, 2 }, right: new[] { 3, 2, 3 }, maxFloors: 2),
            (outward: Vector3.left,    left: new[] { 3, 2, 3 }, right: new[] { 2, 3, 3 }, maxFloors: 3),
        };

        int n = 0;
        foreach (var side in sides)
        {
            var tangent = Vector3.Cross(Vector3.up, side.outward); // runs along the side
            float yaw = Mathf.Atan2(-side.outward.x, -side.outward.z) * Mathf.Rad2Deg; // facade faces the plaza
            foreach (var (widths, start) in new[] { (side.left, -PlazaHalf), (side.right, 1) })
            {
                int u = start;
                foreach (int w in widths)
                {
                    var center = side.outward * (PlazaHalf + HouseDepth / 2f) + tangent * (u + w / 2f);
                    int floors = Mathf.Min(side.maxFloors, Pick(1, 2, 2, 2, 3));
                    BuildHouse(root, $"House_{n++:00}", center, yaw, w, floors);
                    u += w;
                }
            }
        }
    }

    static readonly string[] StoneWindows = { "wall-window-shutters", "wall-window-glass", "wall-window-round", "wall-window-stone" };
    // Not "wall-wood-window-small": unlike every other wall, its corner posts are grey stone, and at a house's corner
    // the facade's post and the side wall's post fill the same spot, so grey and brown flicker through each other.
    // The glass window takes its place twice, so the list stays four long and the rest of the town is unchanged.
    static readonly string[] WoodWindows = { "wall-wood-window-shutters", "wall-wood-window-glass", "wall-wood-window-round", "wall-wood-window-glass" };

    static void BuildHouse(Transform parent, string name, Vector3 center, float yaw, int width, int floors)
    {
        var house = new GameObject(name).transform;
        house.SetParent(parent);
        house.localPosition = center;
        house.localRotation = Quaternion.Euler(0f, yaw, 0f);

        bool stoneBase = rng.NextDouble() < 0.7;
        int doorCol = width / 2;
        int bannerCol = rng.NextDouble() < 0.5 ? (doorCol + 1) % width : -1;

        for (int f = 0; f < floors; f++)
        {
            bool stone = f == 0 ? stoneBase : rng.NextDouble() < 0.25;
            string plain = stone ? "wall" : "wall-wood";
            string[] windows = stone ? StoneWindows : WoodWindows;
            string facadeWindow = Pick(windows);

            for (int i = 0; i < width; i++)
                for (int j = 0; j < HouseDepth; j++)
                {
                    var cell = new Vector3(i - width / 2f + 0.5f, f, j - HouseDepth / 2f + 0.5f);

                    if (j == HouseDepth - 1) // facade (local +Z faces the plaza)
                    {
                        string piece;
                        if (f == 0 && i == doorCol) piece = stone ? "wall-door" : "wall-wood-door";
                        else if (f == 1 && i == bannerCol) piece = plain;
                        else piece = facadeWindow;
                        PlaceEdge(piece, house, cell, Vector3.forward);

                        if (f == 1 && i == bannerCol)
                            PlaceEdge(Pick("banner-red", "banner-green"), house, cell + Vector3.forward, Vector3.back);
                        if (f == 0 && i == doorCol)
                            PlaceEdge("overhang", house, cell + Vector3.forward, Vector3.back);
                    }
                    if (j == 0) PlaceEdge(rng.NextDouble() < 0.3 ? Pick(windows) : plain, house, cell, Vector3.back);
                    if (i == 0) PlaceEdge(rng.NextDouble() < 0.3 ? Pick(windows) : plain, house, cell, Vector3.left);
                    if (i == width - 1) PlaceEdge(rng.NextDouble() < 0.3 ? Pick(windows) : plain, house, cell, Vector3.right);
                }
        }

        // One gabled ridge per facade column, running back from the plaza (row-house look).
        string roof = rng.NextDouble() < 0.5 ? "roof-gable" : "roof-high-gable";
        for (int i = 0; i < width; i++)
            for (int j = 0; j < HouseDepth; j++)
                Place(roof, house, new Vector3(i - width / 2f + 0.5f, floors, j - HouseDepth / 2f + 0.5f), 90f);

        // One solid block, exactly the walls' footprint: the roof's eaves, window sills, banners and the door's
        // overhang all stick out a little, and blocking on those would stop the player short of the wall (and
        // short of the square's barrier across the streets, which lines up with the facades).
        var b = LocalBounds(house.gameObject);
        var c = house.gameObject.AddComponent<BoxCollider>();
        c.center = new Vector3(0f, b.center.y, 0f);
        c.size = new Vector3(width, b.size.y, HouseDepth);
    }

    const float StallCounterWidth = 0.64f;   // unscaled counter depth of stall / stall-red / stall-green
    const float StallScale = 1.3f;
    const float BenchBlockHeight = 1f;       // benches block up to here (m), above the player's 0.7 m
    // Shoppers stand right at the counter: stall centre + half counter + agent radius + a little slack.
    static readonly float ShopOffset = StallCounterWidth * 0.5f * StallScale + 0.22f + 0.05f;
    static readonly float[] WestStallZ = { -4.5f, -2.5f, 2.5f, 4.5f };
    static readonly float[] EastStallZ = { -1.8f, 0.2f, 2.2f };

    static void BuildMarket(Transform env)
    {
        var market = new GameObject("Market").transform;
        market.SetParent(env);

        // West: a row of canopy stalls, stools tucked in the gaps between them.
        for (int i = 0; i < WestStallZ.Length; i++)
            Place(i % 2 == 0 ? "stall-red" : "stall-green", market, new Vector3(-6.8f, 0f, WestStallZ[i]), 0f, 1.3f);
        Place("stall-stool", market, new Vector3(-6.9f, 0f, -3.5f), Range(0, 360));
        Place("stall-stool", market, new Vector3(-6.9f, 0f, 3.5f), Range(0, 360));

        // East: carts and open tables.
        Place("cart-high", market, new Vector3(6.6f, 0f, -4.2f), 15f, 1.3f);
        Place("stall", market, new Vector3(6.8f, 0f, EastStallZ[0]), 0f, 1.3f);
        Place("stall-green", market, new Vector3(6.8f, 0f, EastStallZ[1]), 0f, 1.3f);
        Place("stall", market, new Vector3(6.8f, 0f, EastStallZ[2]), 0f, 1.3f);
        Place("cart", market, new Vector3(6.4f, 0f, 4.6f), -20f, 1.3f);
    }

    // Where villagers shop, sit and gather. Positions match the stalls/benches/fountain above.
    static void BuildActivitySpots()
    {
        var root = new GameObject("ActivitySpots").transform;

        void Spot(ActivitySpot.Kind kind, Vector3 pos, Vector3 facing)
        {
            var go = new GameObject($"{kind}Spot");
            go.transform.SetParent(root);
            go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(facing));
            go.AddComponent<ActivitySpot>().kind = kind;
        }

        foreach (float z in WestStallZ) Spot(ActivitySpot.Kind.Shop, new Vector3(-6.8f + ShopOffset, 0f, z), Vector3.left);
        foreach (float z in EastStallZ) Spot(ActivitySpot.Kind.Shop, new Vector3(6.8f - ShopOffset, 0f, z), Vector3.right);
        Spot(ActivitySpot.Kind.Shop, new Vector3(5.75f, 0f, -4.2f), Vector3.right); // the hay cart

        // Two seats per bench, facing out toward the plaza (benches sit at 45/135/225/315 degrees, radius 3.3).
        for (int i = 0; i < 4; i++)
        {
            float a = (45f + i * 90f) * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            var tangent = new Vector3(-outward.z, 0f, outward.x);
            foreach (float side in new[] { -0.3f, 0.3f })
                Spot(ActivitySpot.Kind.Bench, outward * 3.3f + tangent * side, outward);
        }

        // A ring around the fountain, looking at the water.
        for (int i = 0; i < 8; i++)
        {
            float a = (22.5f + i * 45f) * Mathf.Deg2Rad;
            var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
            Spot(ActivitySpot.Kind.Fountain, outward * 2.3f, -outward);
        }
    }

    // Bakes walkable ground from the environment's colliders, sized for the small Kenney characters.
    static void BakeNavMesh(Transform env)
    {
        var settingsAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/NavMeshAreas.asset")[0];
        var so = new SerializedObject(settingsAsset);
        var humanoid = so.FindProperty("m_Settings").GetArrayElementAtIndex(0);
        humanoid.FindPropertyRelative("agentRadius").floatValue = 0.22f;
        humanoid.FindPropertyRelative("agentHeight").floatValue = 0.7f;
        humanoid.FindPropertyRelative("agentClimb").floatValue = 0.1f;
        so.ApplyModifiedPropertiesWithoutUndo();

        var surface = env.gameObject.AddComponent<NavMeshSurface>();
        surface.agentTypeID = 0;
        surface.collectObjects = CollectObjects.Children;
        surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        surface.BuildNavMesh();

        string path = $"{GenDir}/TownSquareNavMesh.asset";
        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(surface.navMeshData, path);
    }

    static void BuildOutskirts(Transform env)
    {
        var outskirts = new GameObject("Outskirts").transform;
        outskirts.SetParent(env);

        string[] trees = { "tree", "tree-high", "tree-crooked", "tree-high-crooked", "tree-high-round" };
        int placed = 0, tries = 0;
        var used = new List<Vector3>();
        while (placed < 180 && tries++ < 5000)
        {
            var p = new Vector3(Range(-OutskirtsEnd, OutskirtsEnd), 0f, Range(-OutskirtsEnd, OutskirtsEnd));
            float m = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z));
            if (m < PlazaHalf + HouseDepth + 1.2f) continue;               // keep clear of houses
            if (Mathf.Abs(p.x) < 2.5f || Mathf.Abs(p.z) < 2.5f) continue;   // keep streets clear
            if (used.Any(q => (q - p).sqrMagnitude < 4f)) continue;
            used.Add(p);
            Place(Pick(trees), outskirts, p, Range(0, 360), Range(0.9f, 1.4f));
            placed++;
        }
        for (int i = 0; i < 30; i++)
        {
            var p = new Vector3(Range(-OutskirtsEnd, OutskirtsEnd), 0f, Range(-OutskirtsEnd, OutskirtsEnd));
            if (Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.z)) < PlazaHalf + HouseDepth + 1.5f || Mathf.Abs(p.x) < 2.5f || Mathf.Abs(p.z) < 2.5f) continue;
            Place(Pick("rock-small", "rock-wide", "rock-large"), outskirts, p, Range(0, 360), Range(0.5f, 0.9f));
        }

        // Tree clusters filling the four corner gaps between house rows.
        foreach (var sx in new[] { -1, 1 })
            foreach (var sz in new[] { -1, 1 })
                for (int a = 0; a < 2; a++)
                    for (int b = 0; b < 2; b++)
                        Place("tree", outskirts, new Vector3(sx * (PlazaHalf + 0.5f + a), 0f, sz * (PlazaHalf + 0.5f + b)), Range(0, 360), Range(0.8f, 1.1f));
    }

    static void BuildVillagers()
    {
        var root = new GameObject("Villagers").transform;
        var paths = CharacterPaths().Where(p => !ExcludedVillagers.Contains(p)).ToList();
        BuildControllers(paths, out var overrides);

        var spots = new List<Vector3>();
        int tries = 0;
        int count = paths.Count * VillagersPerType;
        Physics.SyncTransforms(); // so the props' colliders are where the props are
        while (spots.Count < count && tries++ < 5000)
        {
            var p = new Vector3(Range(-7f, 7f), 0f, Range(-7f, 7f));
            if (p.magnitude < 2.8f) continue;                        // fountain
            if ((p - PlayerStart).sqrMagnitude < 2.5f) continue;     // player spawn
            if (Mathf.Abs(p.x) > 5.2f && Mathf.Abs(p.z) < 5.5f) continue; // market rows
            // Standing room only: on the walkable nav mesh, and not inside a bench, lantern, tree or stall.
            if (!NavMesh.SamplePosition(p, out var onMesh, 0.1f, NavMesh.AllAreas)) continue;
            if (Physics.CheckCapsule(p + Vector3.up * 0.35f, p + Vector3.up * 0.6f, 0.3f, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (spots.Any(q => (q - p).sqrMagnitude < 1.4f)) continue;
            spots.Add(p);
        }

        for (int i = 0; i < spots.Count; i++)
        {
            string path = paths[i % paths.Count];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            go.name = $"Villager_{i:00}_{Path.GetFileNameWithoutExtension(path)}";
            go.transform.position = spots[i];
            go.transform.rotation = Quaternion.Euler(0f, Range(0, 360), 0f);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = charMat;

            var animator = go.GetComponent<Animator>();
            if (animator == null) animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = overrides[path];
            LoadClip(path, "idle").SampleAnimation(go, 0f); // show the idle pose in the editor instead of a T-pose
            animator.applyRootMotion = false;

            var agent = go.AddComponent<NavMeshAgent>();
            agent.radius = 0.22f;
            agent.height = 0.7f;
            agent.angularSpeed = 400f;
            agent.acceleration = 6f;
            agent.stoppingDistance = 0.05f;
            var standing = go.AddComponent<NavMeshObstacle>();
            standing.shape = NavMeshObstacleShape.Capsule;
            standing.radius = 0.22f;
            standing.height = 0.7f;
            standing.center = new Vector3(0f, 0.35f, 0f);
            standing.enabled = false;
            go.AddComponent<VillagerAI>().skinType = i % paths.Count;

            var body = go.AddComponent<CapsuleCollider>();
            body.radius = 0.22f; body.height = 0.7f; body.center = new Vector3(0f, 0.35f, 0f);
        }
    }

    static readonly string[] VillagerActions = { "sit", "interact-left", "interact-right", "pick-up", "emote-yes", "emote-no", "die" };

    // Base villager controller: a Locomotion blend tree (idle/walk on "Speed") plus one state per action,
    // which VillagerAI cross-fades into by name. Each character gets an override so it plays its own clips.
    static AnimatorController BuildControllers(List<string> paths, out Dictionary<string, RuntimeAnimatorController> overrides)
    {
        string basePath = $"{GenDir}/Villager.controller";
        AssetDatabase.DeleteAsset(basePath);
        string src = paths[0];
        var controller = AnimatorController.CreateAnimatorControllerAtPath(basePath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.CreateBlendTreeInController("Locomotion", out var tree);
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(LoadClip(src, "idle"), 0f);
        tree.AddChild(LoadClip(src, "walk"), 1.1f);

        var sm = controller.layers[0].stateMachine;
        foreach (var action in VillagerActions)
            sm.AddState(action).motion = LoadClip(src, action);

        overrides = new Dictionary<string, RuntimeAnimatorController>();
        foreach (var p in paths)
        {
            string oPath = $"{GenDir}/Villager_{Path.GetFileNameWithoutExtension(p)}.overrideController";
            AssetDatabase.DeleteAsset(oPath);
            var oc = new AnimatorOverrideController(controller);
            foreach (var clip in controller.animationClips.Distinct())
                oc[clip] = LoadClip(p, clip.name);
            AssetDatabase.CreateAsset(oc, oPath);
            overrides[p] = oc;
        }
        return controller;
    }

    static AnimationClip LoadClip(string fbxPath, string clipName) =>
        AssetDatabase.LoadAllAssetsAtPath(fbxPath).OfType<AnimationClip>().First(c => c.name == clipName);

    static GameObject BuildPlayer()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerModel);
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = "Player";
        go.tag = "Player";
        go.transform.SetPositionAndRotation(PlayerStart, Quaternion.identity);
        foreach (var r in go.GetComponentsInChildren<Renderer>()) r.sharedMaterial = playerMat;

        var animator = go.GetComponent<Animator>();
        if (animator == null) animator = go.AddComponent<Animator>();
        animator.runtimeAnimatorController = BuildPlayerController();
        animator.applyRootMotion = false;
        LoadClip(PlayerModel, "idle").SampleAnimation(go, 0f);

        var cc = go.AddComponent<CharacterController>();
        cc.radius = 0.22f;
        cc.height = 0.7f;
        cc.center = new Vector3(0f, 0.36f, 0f);
        cc.skinWidth = 0.02f;
        cc.stepOffset = 0.1f;
        go.AddComponent<PlayerController>();

        // Lets villagers' local avoidance steer around the player.
        var obstacle = go.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Capsule;
        obstacle.radius = 0.25f;
        obstacle.height = 0.7f;
        obstacle.center = new Vector3(0f, 0.35f, 0f);
        return go;
    }

    // Idle -> walk -> sprint, blended by the "Speed" parameter (metres per second).
    static AnimatorController BuildPlayerController()
    {
        string path = $"{GenDir}/Player.controller";
        AssetDatabase.DeleteAsset(path);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(path);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.CreateBlendTreeInController("Locomotion", out var tree);
        tree.blendParameter = "Speed";
        tree.useAutomaticThresholds = false;
        tree.AddChild(LoadClip(PlayerModel, "idle"), 0f);
        tree.AddChild(LoadClip(PlayerModel, "walk"), 1.6f);
        tree.AddChild(LoadClip(PlayerModel, "sprint"), 3.4f);
        return controller;
    }

    // ---------- Collision ----------

    static void AddColliders(Transform env)
    {
        foreach (var t in env.GetComponentsInChildren<Transform>())
        {
            if (!PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) || t.parent == null) continue;
            string n = t.name;
            if (n == "road" || n.StartsWith("banner") || n == "overhang" || t.parent.name.StartsWith("House_")) continue;

            if (n == "Fountain")
            {
                var c = t.gameObject.AddComponent<CapsuleCollider>();
                c.radius = 1f; c.height = 4f; c.center = new Vector3(0f, 1f, 0f);
            }
            else if (n.StartsWith("stall") && n != "stall-bench" && n != "stall-stool")
            {
                // Only the counter blocks; the awning overhangs above head height.
                var b = LocalBounds(t.gameObject);
                var c = t.gameObject.AddComponent<BoxCollider>();
                c.center = new Vector3(0f, b.center.y, 0f);
                c.size = new Vector3(StallCounterWidth, b.size.y, 1f);
            }
            else if (n == "stall-bench")
            {
                // The bench is knee high, and the player's capsule rides up a box that low when it meets a corner
                // at an angle. A block taller than the player can't be climbed. (The intro camera's sight lines
                // ignore benches, so this doesn't count as something in the way.)
                var b = LocalBounds(t.gameObject);
                var c = t.gameObject.AddComponent<BoxCollider>();
                float height = BenchBlockHeight / t.localScale.y;
                c.center = new Vector3(b.center.x, height / 2f, b.center.z);
                c.size = new Vector3(b.size.x, height, b.size.z);
            }
            else if (n.StartsWith("tree") || n == "lantern")
            {
                var c = t.gameObject.AddComponent<CapsuleCollider>();
                c.radius = n == "lantern" ? 0.1f : 0.2f; c.height = 3f; c.center = new Vector3(0f, 1.5f, 0f);
            }
            else
            {
                var b = LocalBounds(t.gameObject);
                var c = t.gameObject.AddComponent<BoxCollider>();
                c.center = b.center; c.size = b.size;
            }
        }

        // Invisible walls along the edge of the square: they close off the streets and corners,
        // and since they're baked into the nav mesh, villagers stay inside too.
        var barrier = new GameObject("SquareBarrier").transform;
        barrier.SetParent(env);
        const float thickness = 0.2f;
        float length = 2f * PlazaHalf + 2f * thickness;
        foreach (var dir in new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right })
        {
            var wall = new GameObject("Wall").AddComponent<BoxCollider>();
            wall.transform.SetParent(barrier);
            wall.transform.position = dir * (PlazaHalf + thickness / 2f) + Vector3.up * 2f;
            wall.size = new Vector3(dir.x != 0 ? thickness : length, 4f, dir.z != 0 ? thickness : length);
        }
    }

    // Bounds of all meshes under `go`, in go's local space (exact for rotated props, unlike Renderer.bounds).
    static Bounds LocalBounds(GameObject go)
    {
        var w2l = go.transform.worldToLocalMatrix;
        bool any = false;
        var result = new Bounds();
        foreach (var mf in go.GetComponentsInChildren<MeshFilter>())
        {
            var mb = mf.sharedMesh.bounds;
            var m = w2l * mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = m.MultiplyPoint3x4(corner);
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; }
                else result.Encapsulate(p);
            }
        }
        return result;
    }

    static void BuildLightingAndCamera(GameObject player)
    {
        var sky = new Color(0.68f, 0.82f, 0.95f);

        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.93f, 0.8f);
        sun.intensity = 1.6f;
        sun.shadows = LightShadows.Soft;
        sun.shadowStrength = 0.75f;
        sun.transform.rotation = Quaternion.Euler(42f, -35f, 0f);
        RenderSettings.sun = sun;

        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.7f, 0.78f, 0.92f);
        RenderSettings.ambientEquatorColor = new Color(0.78f, 0.74f, 0.7f);
        RenderSettings.ambientGroundColor = new Color(0.42f, 0.4f, 0.38f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = sky;
        RenderSettings.fogStartDistance = 28f;
        RenderSettings.fogEndDistance = 65f;

        var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = sky;
        cam.fieldOfView = 40f;
        cam.farClipPlane = 120f;
        camGo.AddComponent<AudioListener>();
        camGo.AddComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        var orbit = camGo.AddComponent<OrbitCamera>();
        orbit.distance = 8.5f;
        orbit.pitch = 35f;
        orbit.targetScreenY = 0.33f;
        KeepTuning(orbit);
        orbit.follow = player.transform;
        orbit.Apply();

        // Post-processing for the soft, saturated low-poly look.
        string profilePath = $"{GenDir}/TownSquareVolume.asset";
        AssetDatabase.DeleteAsset(profilePath);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, profilePath);
        var tone = profile.Add<Tonemapping>(true); tone.mode.Override(TonemappingMode.Neutral);
        var bloom = profile.Add<Bloom>(true); bloom.intensity.Override(0.35f); bloom.threshold.Override(1f);
        var color = profile.Add<ColorAdjustments>(true); color.saturation.Override(12f); color.postExposure.Override(0.15f);
        var vignette = profile.Add<Vignette>(true); vignette.intensity.Override(0.22f);
        foreach (var c in profile.components) AssetDatabase.AddObjectToAsset(c, profile);
        AssetDatabase.SaveAssets();

        var volume = new GameObject("Global Volume").AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
    }

    // ---------- Keeping Inspector tuning across rebuilds ----------

    // Settings on these components (ranges, timings, camera distance...) are copied from the previous build of the
    // scene, so a rebuild doesn't wipe what was tuned in the Inspector. Scene references are set again afterwards.
    static readonly Dictionary<System.Type, string> tuning = new Dictionary<System.Type, string>();

    static void CaptureTuning()
    {
        tuning.Clear();
        if (!File.Exists(ScenePath)) return;
        if (EditorSceneManager.GetActiveScene().path != ScenePath)
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var type in new[] { typeof(ImposterGame), typeof(OrbitCamera) })
        {
            var existing = Object.FindAnyObjectByType(type);
            if (existing != null) tuning[type] = EditorJsonUtility.ToJson(existing);
        }
    }

    static void KeepTuning(Object component)
    {
        if (tuning.TryGetValue(component.GetType(), out var json))
            EditorJsonUtility.FromJsonOverwrite(json, component);
    }

    static void BuildImposterGame(GameObject player)
    {
        var game = new GameObject("Imposter Game").AddComponent<ImposterGame>();
        KeepTuning(game);
        game.player = player.transform;
        game.titleFont = Resources.Load<Font>(ImposterHud.DefaultTitleFont);
        game.uiFont = Resources.Load<Font>(ImposterHud.DefaultUiFont);
        game.strongFont = Resources.Load<Font>(ImposterHud.DefaultStrongFont);
    }

    static void AddSceneToBuild()
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.All(s => s.path != ScenePath))
        {
            scenes.Add(new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }

    static void Capture(Camera cam, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        var rt = new RenderTexture(1600, 900, 24);
        cam.targetTexture = rt;
        cam.Render();
        RenderTexture.active = rt;
        var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
        tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        cam.targetTexture = null;
        RenderTexture.active = null;
        Object.DestroyImmediate(rt);
        Debug.Log("[TownSquareBuilder] Captured " + path);
    }
}
