import type { Application } from "@cislib/management-client";

export interface ApplicationView {
  readonly id: string;
  readonly name: string;
  readonly description?: string;
  readonly revision: string;
  readonly etag: string;
}

/** The browser receives only this explicit view, never an SDK result or credential. */
export function applicationView(application: Application): ApplicationView {
  return Object.freeze({ id: application.applicationId, name: application.metadata.displayName,
    ...(application.metadata.description === undefined ? {} : { description: application.metadata.description }),
    revision: application.metadataRevision, etag: application.metadataETag });
}
