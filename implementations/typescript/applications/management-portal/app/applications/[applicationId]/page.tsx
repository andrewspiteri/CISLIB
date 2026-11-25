import { ApplicationDetail } from "../../../src/components/application-detail";
import type { ApplicationTab } from "../../../src/components/application-detail";
import { listContext, parameter, validApplicationId } from "../../../src/page-input";
import { notFound } from "next/navigation";
import type { PageInput } from "../../../src/page-input";
export default async function ApplicationPage({ params, searchParams }: { params: Promise<{ applicationId: string }>; searchParams: Promise<PageInput> }) {
  const { applicationId } = await params;
  if (!validApplicationId(applicationId)) { notFound(); }
  const input = await searchParams;
  const requestedTab = parameter(input, "tab");
  const tab: ApplicationTab = requestedTab === "configuration" || requestedTab === "capabilities" || requestedTab === "jobs" ? requestedTab : "instances";
  const cursor = parameter(input, "cursor");
  return <ApplicationDetail key={JSON.stringify([applicationId, tab, cursor])} applicationId={applicationId} initialTab={tab} {...(cursor === undefined ? {} : { initialCursor: cursor })} context={listContext(input)} />;
}
