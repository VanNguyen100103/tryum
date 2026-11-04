// Worker phục vụ SPA (Angular) với fallback về index.html cho các route phía client.
export default {
  async fetch(request, env) {
    const url = new URL(request.url);

    // Phục vụ static asset từ binding ASSETS (build Angular trong dist/tryum-frontend/browser).
    let response = await env.ASSETS.fetch(request);

    // 404 (route phía client như /pos, /products/...) → trả index.html để Angular router xử lý.
    if (response.status === 404) {
      response = await env.ASSETS.fetch(
        new Request(`${url.origin}/index.html`, request)
      );
    }

    return response;
  },
};
