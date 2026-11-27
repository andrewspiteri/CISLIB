import { ApplicationDetail } from "../../../src/components/application-detail";
import type { ApplicationTab } from "../../../src/components/application-detail";
import { applicationSelection, listContext, parameter, validRouteParameters } from "../../../src/page-input";
import { isCursor } from "@cislib/management-client";
import { notFound } from "next/navigation";
import type { PageInput } from "../../../src/page-input";
export default async function ApplicationPage({ params, searchParams }: { params: Promise<{ applicationId: string }>; searchParams: Promise<PageInput> }) {
  const path = await params, input = await searchParams;
  const applicationId = applicationSelection(path.applicationId, input);
  if (applicationId === undefined || !validRouteParameters(input, ["tab", "cursor"])) { notFound(); }
  const requestedTab = parameter(input, "tab");
  const tab: ApplicationTab = requestedTab === "configuration" || requestedTab === "capabilities" || requestedTab === "jobs" ? requestedTab : "instances";
  const cursor = parameter(input, "cursor");
  if (cursor !== undefined && !isCursor(cursor)) { notFound(); }
  return <ApplicationDetail key={JSON.stringify([applicationId, tab, cursor])} applicationId={applicationId} initialTab={tab} {...(cursor === undefined ? {} : { initialCursor: cursor })} context={listContext(input)} />;
}
