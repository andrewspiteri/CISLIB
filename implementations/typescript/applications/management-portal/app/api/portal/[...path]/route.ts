import { handlePortalRead } from "../../../../src/server/management.ts";
import { handleMetadata, isMetadataPath } from "../../../../src/server/metadata.ts";
import { handleConfiguration, isConfigurationPath } from "../../../../src/server/configuration.ts";
import { handleRuleEngine, isRuleEnginePath } from "../../../../src/server/rule-engine.ts";
import { handoffPortalResponse } from "../../../../src/portal-response.ts";

export const runtime = "nodejs";
export const dynamic = "force-dynamic";
export async function GET(request: Request, context: { params: Promise<{ path: string[] }> }): Promise<Response> {
  return await handoffPortalResponse(request.signal, async () => {
    const path = (await context.params).path;
    return await (isMetadataPath(path) ? handleMetadata(request, path)
      : isConfigurationPath(path) ? handleConfiguration(request, path)
      : isRuleEnginePath(path) ? handleRuleEngine(request, path) : handlePortalRead(request, path));
  });
}
export async function PUT(request: Request, context: { params: Promise<{ path: string[] }> }): Promise<Response> {
  return await handoffPortalResponse(request.signal, async () => {
    const path = (await context.params).path;
    if (isMetadataPath(path)) { return await handleMetadata(request, path); }
    if (isConfigurationPath(path)) { return await handleConfiguration(request, path); }
    return Response.json({ state: "invalid", message: "This operation is unavailable." }, {
      status: 405, headers: { "Cache-Control": "private, no-store, max-age=0", Pragma: "no-cache", Vary: "Cookie", Allow: "GET" }
    });
  });
}
export async function POST(request: Request, context: { params: Promise<{ path: string[] }> }): Promise<Response> {
  return await handoffPortalResponse(request.signal, async () => {
    const path = (await context.params).path;
    if (isConfigurationPath(path)) { return await handleConfiguration(request, path); }
    if (isRuleEnginePath(path)) { return await handleRuleEngine(request, path); }
    return Response.json({ state: "invalid", message: "This operation is unavailable." }, {
      status: 405, headers: { "Cache-Control": "private, no-store, max-age=0", Pragma: "no-cache", Vary: "Cookie", Allow: "GET" }
    });
  });
}
