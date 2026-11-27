import { notFound } from "next/navigation";
import { MetadataEditor } from "../../../../src/components/metadata-editor";
import { applicationSelection, listContext } from "../../../../src/page-input";
import type { PageInput } from "../../../../src/page-input";

export default async function MetadataPage({ params, searchParams }: { params: Promise<{ applicationId: string }>; searchParams: Promise<PageInput> }) {
  const path = await params, input = await searchParams, applicationId = applicationSelection(path.applicationId, input);
  if (applicationId === undefined) { notFound(); }
  return <MetadataEditor key={applicationId} applicationId={applicationId} context={listContext(input)} />;
}
