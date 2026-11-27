import { notFound } from "next/navigation";
import { JobDetail } from "../../../../../src/components/job-detail";
import { applicationSelection, listContext, parameter, validRouteParameters } from "../../../../../src/page-input";
import type { PageInput } from "../../../../../src/page-input";
import type { JobTarget } from "../../../../../src/components/job-detail";

export default async function JobDetailsPage({ params, searchParams }: {
  params: Promise<{ applicationId: string }>; searchParams: Promise<PageInput>
}) {
  const path = await params, input = await searchParams, applicationId = applicationSelection(path.applicationId, input);
  const fields = ["registration", "report", "scope", "job", "resource", "mode", "cursor", "view"] as const;
  if (applicationId === undefined || !validRouteParameters(input, fields)) { notFound(); }
  const target: JobTarget = Object.fromEntries(fields.flatMap(name => {
    const value = parameter(input, name); return value === undefined ? [] : [[name, value]];
  }));
  return <JobDetail key={JSON.stringify([applicationId, target])} applicationId={applicationId} target={target} context={listContext(input)} />;
}
