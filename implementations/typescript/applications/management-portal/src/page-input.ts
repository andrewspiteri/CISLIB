export type PageInput = Record<string, string | string[] | undefined>;
export function validApplicationId(value: string) { return /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/u.test(value); }
/** URL state selects a view; native authorization is performed again for every data read. */
export function parameter(input: PageInput, name: string): string | undefined {
  const value = input[name];
  if (typeof value !== "string" || value.length === 0 || value.length > 2048 || /[\u0000-\u001f\u007f-\u009f]/u.test(value)) { return undefined; }
  return value;
}
export function listContext(input: PageInput) {
  const fromQuery = parameter(input, "fromQuery");
  const fromEnvironment = parameter(input, "fromEnvironment");
  const fromCursor = parameter(input, "fromCursor");
  return { ...(fromQuery === undefined ? {} : { fromQuery }), ...(fromEnvironment === undefined ? {} : { fromEnvironment }), ...(fromCursor === undefined ? {} : { fromCursor }) };
}
