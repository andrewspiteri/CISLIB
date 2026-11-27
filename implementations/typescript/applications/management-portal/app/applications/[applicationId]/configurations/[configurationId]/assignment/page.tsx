import { notFound } from "next/navigation";
import { isUuid } from "@cislib/management-client";
import { ConfigurationVersionPage } from "../../../../../../src/components/configuration-version-page";
import { configurationSelection, listContext, parameter, validRouteParameters } from "../../../../../../src/page-input";
import type { PageInput } from "../../../../../../src/page-input";

export default async function AssignmentPage({ params, searchParams }: {
  params: Promise<{ applicationId: string; configurationId: string }>; searchParams: Promise<PageInput>
}) {
  const path = await params, input = await searchParams;
  const selected = configurationSelection(path.applicationId, path.configurationId, input, true), releaseId = parameter(input, "releaseId");
  if (selected === undefined || !validRouteParameters(input, ["releaseId"])
    || releaseId !== undefined && !isUuid(releaseId)) { notFound(); }
  const { applicationId, configurationId, schemaVersion } = selected;
  return <ConfigurationVersionPage key={JSON.stringify([applicationId, configurationId, schemaVersion, releaseId])} applicationId={applicationId}
    configurationId={configurationId} schemaVersion={schemaVersion} context={listContext(input)} {...(releaseId === undefined ? {} : { initialReleaseId: releaseId })} />;
}
