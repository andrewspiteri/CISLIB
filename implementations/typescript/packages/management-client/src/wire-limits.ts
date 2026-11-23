export interface ManagementWireLimits {
  readonly maximumRegularRequestBytes: number;
  readonly maximumInventoryRequestBytes: number;
  readonly maximumRegularResponseBytes: number;
  readonly maximumInventoryResponseBytes: number;
  readonly maximumJsonDepth: number;
  readonly maximumModules: number;
  readonly maximumCategories: number;
  readonly maximumJobs: number;
  readonly maximumJobObservations: number;
}

export const defaultWireLimits: ManagementWireLimits = Object.freeze({
  maximumRegularRequestBytes: 65536, maximumInventoryRequestBytes: 4194304,
  maximumRegularResponseBytes: 1048576, maximumInventoryResponseBytes: 8388608,
  maximumJsonDepth: 16, maximumModules: 32, maximumCategories: 64,
  maximumJobs: 1024, maximumJobObservations: 4096
});

const ceilings: ManagementWireLimits = {
  maximumRegularRequestBytes: 1048576, maximumInventoryRequestBytes: 16777216,
  maximumRegularResponseBytes: 8388608, maximumInventoryResponseBytes: 33554432,
  maximumJsonDepth: 32, maximumModules: 1024, maximumCategories: 65536,
  maximumJobs: 65536, maximumJobObservations: 65536
};

export function captureWireLimits(input: Partial<ManagementWireLimits> = {}): ManagementWireLimits {
  const captured = { ...defaultWireLimits, ...input };
  for (const key of Object.keys(ceilings) as (keyof ManagementWireLimits)[]) {
    const value = captured[key];
    if (!Number.isSafeInteger(value) || value < 1 || value > ceilings[key]) {
      throw new RangeError("Select finite management wire limits within the documented ceilings.");
    }
  }
  return Object.freeze(captured);
}
