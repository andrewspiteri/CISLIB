import { isOperationKey, isOrderedInteger, isUuid } from "@cislib/management-client";

export type PageInput = Record<string, string | string[] | undefined>;
export function validApplicationId(value: string) { return isUuid(value); }
/** URL state selects a view; native authorization is performed again for every data read. */
export function parameter(input: PageInput, name: string): string | undefined {
  const value = input[name];
  if (typeof value !== "string" || value.length === 0 || value.length > 2048 || /[\u0000-\u001f\u007f-\u009f]/u.test(value)) { return undefined; }
  return value;
}
/** Duplicate or malformed selectors must not become missing optional parameters. */
export function validRouteParameters(input: PageInput, names: readonly string[]): boolean {
  return names.every(name => input[name] === undefined || parameter(input, name) !== undefined);
}
/** Slugs are cosmetic. Legacy UUID paths remain exact; readable paths require their retained UUID query selector. */
export function applicationSelection(path: string, input: PageInput): string | undefined {
  if (!validRouteParameters(input, ["applicationId"])) { return undefined; }
  const selected = parameter(input, "applicationId");
  if (selected !== undefined && !validApplicationId(selected)) { return undefined; }
  if (validApplicationId(path)) { return selected === undefined || selected === path ? path : undefined; }
  return validCosmeticSlug(path) ? selected : undefined;
}
export function configurationSelection(applicationPath: string, configurationPath: string, input: PageInput, legacy = false): {
  readonly applicationId: string; readonly configurationId: string; readonly schemaVersion: string;
} | undefined {
  if (!validRouteParameters(input, ["applicationId", "configurationId", "schemaVersion"])) { return undefined; }
  const applicationId = applicationSelection(applicationPath, input), selected = parameter(input, "configurationId");
  const configurationId = legacy ? selected === undefined || selected === configurationPath ? configurationPath : undefined
    : validCosmeticSlug(configurationPath) ? selected : undefined;
  const schemaVersion = parameter(input, "schemaVersion");
  return applicationId !== undefined && isOperationKey(configurationId) && isOrderedInteger(schemaVersion)
    ? { applicationId, configurationId, schemaVersion } : undefined;
}
function validCosmeticSlug(value: string): boolean {
  return [...value].length <= 80 && /^[\p{L}\p{N}]+(?:-[\p{L}\p{N}]+)*$/u.test(value);
}
export function listContext(input: PageInput) {
  const fromQuery = parameter(input, "fromQuery");
  const fromEnvironment = parameter(input, "fromEnvironment");
  const fromCursor = parameter(input, "fromCursor");
  return { ...(fromQuery === undefined ? {} : { fromQuery }), ...(fromEnvironment === undefined ? {} : { fromEnvironment }), ...(fromCursor === undefined ? {} : { fromCursor }) };
}
