import { JobDetail } from "../../../../src/components/job-detail";
import type { JobTarget } from "../../../../src/components/job-detail";
import { listContext, parameter, validApplicationId } from "../../../../src/page-input";
import { notFound } from "next/navigation";
import type { PageInput } from "../../../../src/page-input";
export default async function JobPage({ params, searchParams }: { params: Promise<{ applicationId: string }>; searchParams: Promise<PageInput> }) {
  const { applicationId } = await params;
  if (!validApplicationId(applicationId)) { notFound(); }
  const input = await searchParams;
  const target: JobTarget = Object.fromEntries(["registration", "report", "scope", "job", "resource", "mode", "cursor", "view"].flatMap(name => {
    const value = parameter(input, name); return value === undefined ? [] : [[name, value]];
  }));
  return <JobDetail key={JSON.stringify([applicationId, target])} applicationId={applicationId} target={target} context={listContext(input)} />;
}
