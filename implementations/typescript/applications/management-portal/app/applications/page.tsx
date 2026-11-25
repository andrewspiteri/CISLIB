import { Applications } from "../../src/components/applications";
import { parameter } from "../../src/page-input";
import type { PageInput } from "../../src/page-input";
export default async function ApplicationsPage({ searchParams }: { searchParams: Promise<PageInput> }) {
  const input = await searchParams;
  const query = parameter(input, "query") ?? "";
  const environment = parameter(input, "environment") ?? "";
  const cursor = parameter(input, "cursor");
  return <Applications key={JSON.stringify([query, environment, cursor])} initialQuery={query} initialEnvironment={environment} {...(cursor === undefined ? {} : { initialCursor: cursor })} />;
}
