const canceledHeaders = Object.freeze({
  "Cache-Control": "private, no-store, max-age=0",
  Pragma: "no-cache",
  Vary: "Cookie"
});

/** Retain admitted work and discard its unsent body if cancellation is already known at handoff. */
export async function handoffPortalResponse(signal: AbortSignal, execute: () => Promise<Response>): Promise<Response> {
  if (signal.aborted) { return canceledResponse(); }
  const response = await execute();
  if (!signal.aborted) { return response; }
  // The callback has settled, including its native cleanup. Its response has
  // not been handed to Next, so cancellation owns this still-unconsumed body.
  // A body-cancellation failure remains a failure; it cannot become a clean 499.
  if (response.body !== null) { await response.body.cancel(); }
  return canceledResponse();
}

function canceledResponse(): Response {
  return new Response(null, { status: 499, headers: canceledHeaders });
}
