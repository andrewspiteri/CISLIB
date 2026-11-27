import { notFound } from "next/navigation";
import { isCursor, isUuid } from "@cislib/management-client";
import { ConfigurationVersionPage } from "../../../../../../src/components/configuration-version-page";
import { configurationSelection, listContext, parameter, validRouteParameters } from "../../../../../../src/page-input";
import type { PageInput } from "../../../../../../src/page-input";

export default async function ConfigurationEditorPage({ params, searchParams }: {
  params: Promise<{ applicationId: string; configurationSlug: string }>; searchParams: Promise<PageInput>
}) {
  const path = await params, input = await searchParams, selected = configurationSelection(path.applicationId, path.configurationSlug, input);
  const source = parameter(input, "source") ?? "application", publicationId = parameter(input, "publicationId"), cursor = parameter(input, "cursor");
  if (selected === undefined || !validRouteParameters(input, ["source", "publicationId", "cursor"])
    || source !== "common" && source !== "application" || publicationId !== undefined && !isUuid(publicationId) || cursor !== undefined && !isCursor(cursor)) { notFound(); }
  return <ConfigurationVersionPage key={JSON.stringify([selected.applicationId, selected.configurationId, selected.schemaVersion])}
    applicationId={selected.applicationId} configurationId={selected.configurationId} schemaVersion={selected.schemaVersion}
    context={listContext(input)} {...(cursor === undefined ? {} : { initialCursor: cursor })} />;
}
