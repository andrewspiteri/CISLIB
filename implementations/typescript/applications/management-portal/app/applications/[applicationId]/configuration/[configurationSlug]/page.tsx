import { notFound } from "next/navigation";
import { isCursor, isUuid } from "@cislib/management-client";
import { ConfigurationVersionPage } from "../../../../../src/components/configuration-version-page";
import { configurationSelection, listContext, parameter, validRouteParameters } from "../../../../../src/page-input";
import type { PageInput } from "../../../../../src/page-input";

export default async function ConfigurationDetailsPage({ params, searchParams }: {
  params: Promise<{ applicationId: string; configurationSlug: string }>; searchParams: Promise<PageInput>
}) {
  const path = await params, input = await searchParams;
  const selected = configurationSelection(path.applicationId, path.configurationSlug, input);
  const source = parameter(input, "source"), cursor = parameter(input, "cursor"), releaseId = parameter(input, "releaseId");
  if (selected === undefined || !validRouteParameters(input, ["source", "cursor", "releaseId"]) || source !== undefined && source !== "common" && source !== "application"
    || cursor !== undefined && !isCursor(cursor) || releaseId !== undefined && !isUuid(releaseId)) { notFound(); }
  return <ConfigurationVersionPage key={JSON.stringify([selected.applicationId, selected.configurationId, selected.schemaVersion])}
    applicationId={selected.applicationId} configurationId={selected.configurationId} schemaVersion={selected.schemaVersion}
    {...(releaseId === undefined ? {} : { initialReleaseId: releaseId })}
    {...(cursor === undefined ? {} : { initialCursor: cursor })} context={listContext(input)} />;
}
