import { NextRequest } from "next/server";
import { handleAuth } from "../../../../src/server/auth.ts";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";
type Context = { params: Promise<{ nextauth: string[] }> };
async function handler(request: NextRequest, context: Context): Promise<Response> {
  try { return await handleAuth(request, context); }
  catch {
    console.error("[PortalAuth] Authentication boundary failed.");
    return new Response("Authentication is unavailable.", { status: 503, headers: { "Cache-Control": "private, no-store, max-age=0" } });
  }
}
export { handler as GET, handler as POST };
