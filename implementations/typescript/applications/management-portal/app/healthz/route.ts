/** Public liveness is a cacheable constant. It never probes either database. */
export const dynamic = "force-static";
export const revalidate = 5;
export function GET(): Response {
  return Response.json({ status: "alive" }, { headers: { "Cache-Control": "public, max-age=5, must-revalidate" } });
}
