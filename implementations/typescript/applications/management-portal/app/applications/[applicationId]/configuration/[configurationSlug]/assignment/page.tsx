import { notFound } from "next/navigation";
import { isUuid } from "@cislib/management-client";
import { ConfigurationVersionPage } from "../../../../../../src/components/configuration-version-page";
import { configurationSelection, listContext, parameter, validRouteParameters } from "../../../../../../src/page-input";
import type { PageInput } from "../../../../../../src/page-input";

export default async function ConfigurationAssignmentPage({ params, searchParams }: {
  params: Promise<{ applicationId: string; configurationSlug: string }>; searchParams: Promise<PageInput>
}) {
  const path = await params, input = await searchParams, selected = configurationSelection(path.applicationId, path.configurationSlug, input);
  const releaseId = parameter(input, "releaseId");
  if (selected === undefined || !validRouteParameters(input, ["releaseId"]) || releaseId !== undefined && !isUuid(releaseId)) { notFound(); }
  return <ConfigurationVersionPage key={JSON.stringify([selected.applicationId, selected.configurationId, selected.schemaVersion, releaseId])}
    applicationId={selected.applicationId} configurationId={selected.configurationId} schemaVersion={selected.schemaVersion}
    context={listContext(input)} {...(releaseId === undefined ? {} : { initialReleaseId: releaseId })} />;
}
