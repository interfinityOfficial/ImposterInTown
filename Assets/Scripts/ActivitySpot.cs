using System.Collections.Generic;
using UnityEngine;

// A place where a villager can do something. The spot's forward (blue arrow) is the way they face.
// For benches, the transform is the seat; villagers walk to a point in front of it, then sit.
public class ActivitySpot : MonoBehaviour
{
    public enum Kind { Shop, Bench, Fountain }

    public Kind kind;
    [HideInInspector] public VillagerAI occupant;

    public static readonly List<ActivitySpot> All = new List<ActivitySpot>();

    void OnEnable() => All.Add(this);
    void OnDisable() => All.Remove(this);

    // Where to stand: on the spot itself, or just in front of the seat for benches, as close as the nav mesh
    // allows (the bench's half depth, 0.17 m, plus a villager's 0.22 m radius), so the slide onto the seat is short.
    public Vector3 ApproachPoint => kind == Kind.Bench ? transform.position + transform.forward * BenchApproach : transform.position;
    const float BenchApproach = 0.42f;

    void OnDrawGizmos()
    {
        Gizmos.color = kind == Kind.Shop ? Color.yellow : kind == Kind.Bench ? Color.cyan : Color.blue;
        Gizmos.DrawWireSphere(transform.position, 0.15f);
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 0.4f);
    }
}
