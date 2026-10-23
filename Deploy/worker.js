// Serves the Unity web build (../Build) from Cloudflare.
// Unity's build files are already Brotli-compressed (*.br), and the browser only unpacks them itself when the
// response says "Content-Encoding: br". Cloudflare's static assets don't add that, so the files in /Build/
// come through here: same file, plus the right headers, sent exactly as stored (encodeBody: "manual", so
// Cloudflare doesn't compress it a second time). Everything else (the page, icons, share image) is served as is.

const TYPES = {
  ".wasm.br": "application/wasm",
  ".js.br": "application/javascript",
  ".data.br": "application/octet-stream",
  ".symbols.json.br": "application/json",
};

export default {
  async fetch(request, env) {
    const response = await env.ASSETS.fetch(request);
    const path = new URL(request.url).pathname;
    if (!response.ok || !path.endsWith(".br")) return response;

    const headers = new Headers(response.headers);
    headers.set("Content-Encoding", "br");
    const suffix = Object.keys(TYPES).find((s) => path.endsWith(s));
    headers.set("Content-Type", suffix ? TYPES[suffix] : "application/octet-stream");
    // the file names don't change between builds, so browsers check back for a newer copy each visit
    headers.set("Cache-Control", "public, max-age=0, must-revalidate");
    return new Response(response.body, { status: response.status, headers, encodeBody: "manual" });
  },
};
