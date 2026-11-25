import { handlePortalRead } from "../../../../src/server/management.ts";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";
export async function GET(request: Request, context: { params: Promise<{ path: string[] }> }): Promise<Response> {
  return await handlePortalRead(request, (await context.params).path);
}
