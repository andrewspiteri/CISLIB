import "server-only";
import type { RuleEngineCallResult, RuleVersionSaveResult, RuleDeploymentOperationResult, RuleCounterResetOperationResult } from "@cislib/management-client";
import { isCursor, isOperationKey, isStrongETag, isUuid } from "@cislib/management-client";
import { isRuleSelector, ruleNote } from "../rule-engine-draft.ts";
import { readRuleExecutionFilters, ruleExecutionFilterKeys } from "../rule-engine-routes.ts";
import { projectRuleOperation } from "../rule-engine-operations.ts";
import type { RuleOperationFamily, RuleOperationTarget } from "../rule-engine-operations.ts";
import { PortalRequestInvalid, readPortalJson, withPortalCall } from "./portal-call.ts";
import type { PortalCallScope } from "./portal-call.ts";

export function isRuleEnginePath(path: readonly string[]) {
  return path[0] === "applications" && path[2] === "rule-engine";
}

export function handleRuleEngine(request: Request, path: readonly string[]): Promise<Response> {
  const mutating = request.method === "POST";
  return withPortalCall(request, { mutating, requireContext: mutating }, async scope => {
    const applicationId = path[1], route = path[3], query = new URL(request.url).searchParams;
    const client = scope.runtime.ruleEngine;
    if (!isUuid(applicationId) || !isRuleEnginePath(path) || path.length > 5 || !mutating && (request.method !== "GET" || request.body !== null)) {
      throw new PortalRequestInvalid();
    }
    if (route === undefined && !mutating) {
      return read(scope, await client.listRulesets(applicationId, page(query, []), scope.context));
    }
    if (path.length === 4 && route === "capabilities" && !mutating) {
      onlyQuery(query, []);
      return readCapabilities(scope, applicationId);
    }
    const rulesetId = query.get("rulesetId");
    if (!isRuleSelector(rulesetId)) { throw new PortalRequestInvalid("Select one exact ruleset."); }
    const target = { applicationId, rulesetId };
    if (path.length === 4 && !mutating) {
      switch (route) {
        case "executions": {
          const selected = page(query, ["rulesetId", ...ruleExecutionFilterKeys]);
          let filters;
          try { filters = readRuleExecutionFilters(key => query.get(key) ?? undefined); }
          catch { throw new PortalRequestInvalid("Use exact execution filters and an ordered UTC interval."); }
          return read(scope, await client.listExecutions(applicationId, rulesetId, filters, selected, scope.context));
        }
        case "execution": {
          onlyQuery(query, ["rulesetId", "executionId"]);
          const id = query.get("executionId");
          if (!isUuid(id)) { throw new PortalRequestInvalid("Select one exact execution."); }
          return read(scope, await client.getExecution(applicationId, rulesetId, id, scope.context));
        }
        case "counters": return read(scope, await client.listCounters(applicationId, rulesetId, page(query, ["rulesetId"]), scope.context));
        case "context": {
          onlyQuery(query, ["rulesetId"]);
          const result = await client.listRulesets(applicationId, { pageSize: 256 }, scope.context);
          if (result.kind !== "http" || result.status !== 200 || !result.cleanupSucceeded || result.value === undefined) { return read(scope, result); }
          if (result.value.nextCursor !== undefined) { return scope.failure("unavailable", "The exact ruleset could not be resolved.", 503); }
          const selected = result.value.items.find(item => item.rulesetId === rulesetId);
          return selected === undefined ? scope.failure("not-found", "The selected ruleset is unavailable.", 404) : scope.ready(selected);
        }
        case "schema": {
          onlyQuery(query, ["rulesetId", "schemaRevision"]);
          const revision = query.get("schemaRevision");
          if (!isRuleSelector(revision)) { throw new PortalRequestInvalid("Select one exact rule schema."); }
          return read(scope, await client.getSchema(applicationId, rulesetId, revision, scope.context));
        }
        case "versions": return read(scope, await client.listVersions(applicationId, rulesetId, page(query, ["rulesetId"]), scope.context));
        case "version": {
          onlyQuery(query, ["rulesetId", "version"]);
          const version = query.get("version");
          if (!isUuid(version)) { throw new PortalRequestInvalid("Select one exact saved version."); }
          return read(scope, await client.getVersion(applicationId, rulesetId, version, scope.context));
        }
        case "deployment":
          onlyQuery(query, ["rulesetId"]);
          return read(scope, await client.getDeployment(applicationId, rulesetId, scope.context));
        case "deployment-instances":
          return read(scope, await client.listDeploymentInstances(applicationId, rulesetId, page(query, ["rulesetId"]), scope.context));
      }
    }
    if (path.length === 5 && !mutating && (route === "counter-reset-operations" || route === "counter-reset-outcomes")) {
      const key = path[4];
      if (!isOperationKey(key)) { throw new PortalRequestInvalid("Select the original reset key."); }
      if (route === "counter-reset-outcomes") {
        return read(scope, await client.listCounterResetOutcomes(applicationId, rulesetId, key, page(query, ["rulesetId"]), scope.context));
      }
      onlyQuery(query, ["rulesetId"]);
      return command(scope, await client.getCounterResetOperation(applicationId, rulesetId, key, scope.context),
        "management.rule-engine.counter.reset", target, key);
    }
    if (path.length === 5 && !mutating && (route === "version-operations" || route === "deployment-operations")) {
      onlyQuery(query, ["rulesetId"]);
      const key = path[4];
      if (!isOperationKey(key)) { throw new PortalRequestInvalid("Select the original operation key."); }
      return route === "version-operations"
        ? command(scope, await client.getSaveOperation(applicationId, rulesetId, key, scope.context), "management.rule-engine.version.save", target, key)
        : command(scope, await client.getDeploymentOperation(applicationId, rulesetId, key, scope.context), "management.rule-engine.deployment.request", target, key);
    }
    if (path.length === 4 && mutating && route === "counter-resets") {
      onlyQuery(query, ["rulesetId"]);
      const key = request.headers.get("idempotency-key");
      if (!isOperationKey(key) || request.headers.has("if-match")) { throw new PortalRequestInvalid("Supply the original counter reset key."); }
      const body = JSON.parse(await readPortalJson(request, scope.signal, 16384)) as Record<string, unknown>;
      const ids = body.ruleIds;
      if (!Object.keys(body).every(field => ["selection", "ruleIds", "reason"].includes(field)) || !ruleNote(body.reason)
        || !Array.isArray(ids) || ids.length > 48 || !ids.every(isRuleSelector) || new Set(ids).size !== ids.length
        || !(body.selection === "one" && ids.length === 1 || body.selection === "selected" && ids.length > 0
          || body.selection === "all" && ids.length === 0)) {
        throw new PortalRequestInvalid("Supply one exact counter reset scope and a bounded reason.");
      }
      return command(scope, await client.resetCounters(applicationId, rulesetId, key, {
        selection: body.selection, ruleIds: ids, reason: body.reason
      }, scope.context), "management.rule-engine.counter.reset", target, key);
    }
    if (path.length === 4 && mutating && (route === "versions" || route === "deployments")) {
      onlyQuery(query, ["rulesetId"]);
      const key = request.headers.get("idempotency-key"), ifMatch = request.headers.get("if-match");
      if (!isOperationKey(key) || !isStrongETag(ifMatch)) { throw new PortalRequestInvalid("Supply the original key and current revision."); }
      const body = JSON.parse(await readPortalJson(request, scope.signal, route === "versions" ? 1024 * 1024 : 8192)) as Record<string, unknown>;
      if (!ruleNote(body.reason)) { throw new PortalRequestInvalid("Enter a bounded note or reason."); }
      if (route === "versions") {
        if (!Object.keys(body).every(field => ["schemaRevision", "definitionJson", "baseVersion", "reason"].includes(field))
          || !isRuleSelector(body.schemaRevision) || typeof body.definitionJson !== "string"
          || body.baseVersion !== undefined && !isUuid(body.baseVersion)) { throw new PortalRequestInvalid("Supply one complete version and its exact schema."); }
        return command(scope, await client.saveVersion(applicationId, rulesetId, key, ifMatch, {
          schemaRevision: body.schemaRevision, definitionJson: body.definitionJson, reason: body.reason,
          ...(body.baseVersion === undefined ? {} : { baseVersion: body.baseVersion })
        }, scope.context), "management.rule-engine.version.save", target, key);
      }
      if (!Object.keys(body).every(field => ["version", "intent", "reason"].includes(field)) || !isUuid(body.version)
        || body.intent !== "deploy" && body.intent !== "rollback") { throw new PortalRequestInvalid("Supply one exact saved version and deployment intention."); }
      return command(scope, await client.requestDeployment(applicationId, rulesetId, key, ifMatch,
        { version: body.version, intent: body.intent, reason: body.reason }, scope.context), "management.rule-engine.deployment.request", target, key);
    }
    return scope.failure("not-found", "This Rule Engine operation is unavailable.", 404);
  });
}

async function readCapabilities(scope: PortalCallScope, applicationId: string): Promise<Response> {
  const first = await scope.runtime.management.listApplicationApiCapabilities(applicationId, { pageSize: 64 }, scope.context);
  if (first.kind !== "http" || first.status !== 200 || !first.cleanupSucceeded || first.value === undefined) { return read(scope, first); }
  const relevant = new Set(["listRuleEngineRulesets", "saveRuleEngineVersion", "getRuleEngineVersionOperation", "getRuleEngineDeployment",
    "getRuleEngineDeploymentInstances", "requestRuleEngineDeployment", "getRuleEngineDeploymentOperation",
    "listRuleEngineCounters", "resetRuleEngineCounters", "getRuleEngineCounterResetOperation", "listRuleEngineCounterResetOutcomes",
    "listRuleEngineExecutions", "getRuleEngineExecution"]);
  const values = [...first.value.items];
  if (first.value.nextCursor !== undefined) {
    const next = await scope.runtime.management.listApplicationApiCapabilities(applicationId, { pageSize: 64, cursor: first.value.nextCursor }, scope.context);
    if (next.kind !== "http" || next.status !== 200 || !next.cleanupSucceeded || next.value === undefined) { return read(scope, next); }
    if (next.value.nextCursor !== undefined) { return scope.failure("unavailable", "The application grants could not be verified.", 503); }
    values.push(...next.value.items);
  }
  return scope.ready(values.filter(item => relevant.has(item.feature.operationId)).map(item => ({ operationId: item.feature.operationId, allowed: item.allowed })));
}

function read<T>(scope: PortalCallScope, call: Pick<RuleEngineCallResult<T>, "kind" | "status" | "cleanupSucceeded" | "value">): Response {
  if (call.kind === "http" && call.status === 200 && call.cleanupSucceeded && call.value !== undefined) { return scope.ready(call.value); }
  if (call.status === 401) { return scope.failure("unauthenticated", "Sign in to continue.", 401); }
  if (call.status === 403) { return scope.failure("denied", "Access is unavailable for this target.", 403); }
  if (call.status === 404) { return scope.failure("not-found", "The selected ruleset or operation is unavailable.", 404); }
  if (call.status === 400 || call.status === 422) { return scope.failure("invalid", "The selected query changed. Return to the first page.", 400); }
  return scope.failure("unavailable", "The current Rule Engine state could not be read.", call.kind === "timeout" ? 504 : 503);
}
function command(scope: PortalCallScope, call: RuleEngineCallResult<RuleVersionSaveResult | RuleDeploymentOperationResult | RuleCounterResetOperationResult>,
  family: RuleOperationFamily, target: RuleOperationTarget, key: string): Response {
  if (call.status === 401) { return scope.failure("unauthenticated", "Sign in to continue.", 401); }
  if (call.status === 403 || call.status === 404 && call.problem?.code === "management.rule-engine.not_found") {
    return scope.failure("denied", "Access is unavailable for this operation.", 403);
  }
  return scope.ready(projectRuleOperation(call, family, target, key));
}
function onlyQuery(query: URLSearchParams, keys: readonly string[]) {
  for (const key of query.keys()) { if (!keys.includes(key) || query.getAll(key).length !== 1) { throw new PortalRequestInvalid(); } }
}
function page(query: URLSearchParams, keys: readonly string[]) {
  onlyQuery(query, [...keys, "cursor", "pageSize"]);
  const cursor = query.get("cursor"), size = query.get("pageSize");
  if (cursor !== null && !isCursor(cursor) || size !== null && (!/^[1-9][0-9]{0,2}$/u.test(size) || Number(size) > 100)) {
    throw new PortalRequestInvalid("Select a bounded page and its original cursor.");
  }
  return { pageSize: size === null ? 25 : Number(size), ...(cursor === null ? {} : { cursor }) };
}
