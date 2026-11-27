import { notFound } from "next/navigation";
import { isCursor } from "@cislib/management-client";
import { ConfigurationAdoption } from "../../../../../../src/components/configuration-adoption";
import { configurationSelection, listContext, parameter, validRouteParameters } from "../../../../../../src/page-input";
import type { PageInput } from "../../../../../../src/page-input";

export default async function ConfigurationAdoptionPage({ params, searchParams }: {
  params: Promise<{ applicationId: string; configurationId: string }>; searchParams: Promise<PageInput>
}) {
  const path = await params, input = await searchParams;
  const selected = configurationSelection(path.applicationId, path.configurationId, input, true);
  const cursor = parameter(input, "cursor");
  if (selected === undefined || !validRouteParameters(input, ["cursor"])
    || cursor !== undefined && !isCursor(cursor)) { notFound(); }
  const { applicationId, configurationId, schemaVersion } = selected;
  return <ConfigurationAdoption key={JSON.stringify([applicationId, configurationId, schemaVersion])} applicationId={applicationId}
    configurationId={configurationId} schemaVersion={schemaVersion} context={listContext(input)} {...(cursor === undefined ? {} : { initialCursor: cursor })} />;
}
