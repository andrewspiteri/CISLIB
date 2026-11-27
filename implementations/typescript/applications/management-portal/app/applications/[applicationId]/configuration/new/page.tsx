import { notFound } from "next/navigation";
import { isCursor } from "@cislib/management-client";
import { NewConfiguration } from "../../../../../src/components/new-configuration";
import { applicationSelection, listContext, parameter, validRouteParameters } from "../../../../../src/page-input";
import type { PageInput } from "../../../../../src/page-input";

export default async function NewConfigurationPage({ params, searchParams }: {
  params: Promise<{ applicationId: string }>; searchParams: Promise<PageInput>
}) {
  const path = await params, input = await searchParams, applicationId = applicationSelection(path.applicationId, input);
  const cursor = parameter(input, "cursor");
  if (applicationId === undefined || input.configurationId !== undefined || input.schemaVersion !== undefined || input.source !== undefined
    || !validRouteParameters(input, ["cursor"]) || cursor !== undefined && !isCursor(cursor)) { notFound(); }
  return <NewConfiguration key={applicationId} applicationId={applicationId} {...(cursor === undefined ? {} : { initialCursor: cursor })} context={listContext(input)} />;
}
