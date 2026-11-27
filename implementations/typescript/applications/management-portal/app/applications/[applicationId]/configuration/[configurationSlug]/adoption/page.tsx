import { notFound } from "next/navigation";
import { isCursor } from "@cislib/management-client";
import { ConfigurationAdoption } from "../../../../../../src/components/configuration-adoption";
import { configurationSelection, listContext, parameter, validRouteParameters } from "../../../../../../src/page-input";
import type { PageInput } from "../../../../../../src/page-input";

export default async function ConfigurationAdoptionPage({ params, searchParams }: {
  params: Promise<{ applicationId: string; configurationSlug: string }>; searchParams: Promise<PageInput>
}) {
  const path = await params, input = await searchParams, selected = configurationSelection(path.applicationId, path.configurationSlug, input);
  const cursor = parameter(input, "cursor");
  if (selected === undefined || !validRouteParameters(input, ["cursor"]) || cursor !== undefined && !isCursor(cursor)) { notFound(); }
  return <ConfigurationAdoption key={JSON.stringify([selected.applicationId, selected.configurationId, selected.schemaVersion])}
    applicationId={selected.applicationId} configurationId={selected.configurationId} schemaVersion={selected.schemaVersion}
    context={listContext(input)} {...(cursor === undefined ? {} : { initialCursor: cursor })} />;
}
