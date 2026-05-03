// Shared initialized state — set by api.ts, read by debug.ts.
// Keyed by game name (e.g. 'ets2', 'ats', 'promods').
import type { LoadedGraph } from './types';
import type { SpatialIndex } from './spatial-index';

export interface GraphState {
  graph: LoadedGraph;
  spatialIndex: SpatialIndex;
}

const _states: Record<string, GraphState> = {};

export function setGraphState(game: string, state: GraphState): void {
  _states[game] = state;
}

export function getGraphState(game: string): GraphState | null {
  return _states[game] ?? null;
}

export function getAvailableGames(): string[] {
  return Object.keys(_states);
}
