import { createHash, randomUUID } from "node:crypto";
import { readFile, rename, unlink, writeFile } from "node:fs/promises";
import { createRequire } from "node:module";
import { dirname, join } from "node:path";

// One reviewed correction for the pinned framework. Unknown versions/bytes fail
// closed; this is not a fuzzy dependency patch or a runtime version upgrade.
const require = createRequire(import.meta.url);
const manifest = require.resolve("next/package.json");
const sourceFile = join(dirname(manifest), "dist/server/pipe-readable.js");
const originalSha = "bedc0f8cafeb73b6e48dcfd509ca21b42b9b2a5234aa7925a2283185ae486d07";
const correctedSha = "a41ad25a8622cf75beec29c1f62a265aad76da33c12f23d9f049d7db6b1d28b2";
const before = "    res.once('finish', ()=>{";
const after = "    // An unfinished native close must also release an in-flight writer.close.\n"
  + "    // The existing response AbortController preserves its aborted outcome.\n"
  + "    res.once('close', ()=>{\n"
  + "        finished.resolve();\n"
  + "    });\n"
  + before;

/** Build-time only; retain Next's producer waitUntil and actual abort behavior. */
export async function prepareNextResponseDrain(): Promise<void> {
  const bytes = await readPinnedSource();
  const current = sha(bytes);
  if (current === correctedSha) { return; }
  if (current !== originalSha) { throw new Error("The pinned Next response source is not the reviewed candidate."); }
  const text = new TextDecoder("utf-8", { fatal: true }).decode(bytes);
  if (text.split(before).length !== 2) { throw new Error("The pinned Next response correction is unavailable."); }
  const corrected = Buffer.from(text.replace(before, after), "utf8");
  if (sha(corrected) !== correctedSha) { throw new Error("The Next response correction did not match its reviewed bytes."); }
  const temporary = sourceFile + ".cis-response-drain-" + randomUUID() + ".tmp";
  let originalError: unknown;
  try {
    await writeFile(temporary, corrected, { flag: "wx" });
    await rename(temporary, sourceFile);
  } catch (error) { originalError = error; throw error; }
  finally {
    try { await unlink(temporary); }
    catch (error) {
      if (typeof error !== "object" || error === null || !("code" in error) || error.code !== "ENOENT") {
        throw originalError === undefined ? error : new AggregateError([originalError, error], "Next correction and temporary-file cleanup failed.");
      }
    }
  }
  await requireNextResponseDrain();
}

/** Startup verifies only: deployed dependencies may remain read-only. */
export async function requireNextResponseDrain(): Promise<void> {
  if (sha(await readPinnedSource()) !== correctedSha) {
    throw new Error("Build the portal to prepare its pinned Next response-close correction.");
  }
}
async function readPinnedSource(): Promise<Buffer> {
  const value: unknown = JSON.parse(await readFile(manifest, "utf8"));
  if (typeof value !== "object" || value === null || !("version" in value) || value.version !== "16.4.6") {
    throw new Error("The portal requires its reviewed Next version.");
  }
  return await readFile(sourceFile);
}
function sha(value: Uint8Array): string { return createHash("sha256").update(value).digest("hex"); }
