import { notFound } from "next/navigation";
import { isCursor } from "@cislib/management-client";
import { ApplicationDetail } from "../../../../src/components/application-detail";
import { applicationSelection, listContext, parameter, validRouteParameters } from "../../../../src/page-input";
import type { PageInput } from "../../../../src/page-input";

export default async function ApplicationViewPage({ params, searchParams }: {
  params: Promise<{ applicationId: string }>; searchParams: Promise<PageInput>
}) {
  const path = await params, input = await searchParams;
  const applicationId = applicationSelection(path.applicationId, input), cursor = parameter(input, "cursor"), tab = parameter(input, "tab");
  if (applicationId === undefined || !validRouteParameters(input, ["applicationId", "tab", "cursor"])
    || tab !== undefined && tab !== "capabilities" || cursor !== undefined && !isCursor(cursor)) { notFound(); }
  return <ApplicationDetail key={JSON.stringify([applicationId, "capabilities", cursor])} applicationId={applicationId} initialTab="capabilities"
    {...(cursor === undefined ? {} : { initialCursor: cursor })} context={listContext(input)} />;
}
