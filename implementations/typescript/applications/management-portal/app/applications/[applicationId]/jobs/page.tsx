import { JobDetail } from "../../../../src/components/job-detail";
import type { JobTarget } from "../../../../src/components/job-detail";
import { ApplicationDetail } from "../../../../src/components/application-detail";
import { isCursor } from "@cislib/management-client";
import { applicationSelection, listContext, parameter, validRouteParameters } from "../../../../src/page-input";
import { notFound } from "next/navigation";
import type { PageInput } from "../../../../src/page-input";
export default async function JobPage({ params, searchParams }: { params: Promise<{ applicationId: string }>; searchParams: Promise<PageInput> }) {
  const path = await params, input = await searchParams, applicationId = applicationSelection(path.applicationId, input);
  const fields = ["registration", "report", "scope", "job", "resource", "mode", "cursor", "view"] as const;
  if (applicationId === undefined || !validRouteParameters(input, [...fields, "tab"])) { notFound(); }
  const detail = fields.some(name => name !== "cursor" && input[name] !== undefined);
  const cursor = parameter(input, "cursor"), tab = parameter(input, "tab");
  if (!detail) {
    if (tab !== undefined && tab !== "jobs" || cursor !== undefined && !isCursor(cursor)) { notFound(); }
    return <ApplicationDetail key={JSON.stringify([applicationId, "jobs", cursor])} applicationId={applicationId} initialTab="jobs"
      {...(cursor === undefined ? {} : { initialCursor: cursor })} context={listContext(input)} />;
  }
  const target: JobTarget = Object.fromEntries(fields.flatMap(name => {
    const value = parameter(input, name); return value === undefined ? [] : [[name, value]];
  }));
  return <JobDetail key={JSON.stringify([applicationId, target])} applicationId={applicationId} target={target} context={listContext(input)} />;
}
