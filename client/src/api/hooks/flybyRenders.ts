// SPDX-License-Identifier: AGPL-3.0-or-later
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { api } from '../client.ts';
import type { components } from '../schema';
import { queryKeys } from './queryKeys.ts';
import { unwrap, unwrapVoid } from './shared.ts';

// A flyby rendered on the server, and the render nodes that do it: the shapes, and the hooks.
//
// The server never renders anything itself. A render node — a machine its operator runs a render
// worker on — takes the job over HTTP; the page only asks for a render, follows it, stops it and
// downloads its film.

export type FlybyRender = components['schemas']['FlybyRenderDto'];
export type FlybyRenderStarted = components['schemas']['FlybyRenderStartedDto'];
export type FlybyRenderPlan = components['schemas']['FlybyRenderPlan'];
export type FlybyRenderContainer = components['schemas']['FlybyRenderContainer'];
export type FlybyRenderStatus = components['schemas']['FlybyRenderStatus'];
export type FlybyRenderFile = components['schemas']['FlybyRenderFileDto'];
export type RenderNode = components['schemas']['RenderNodeDto'];
export type RenderNodeIssued = components['schemas']['RenderNodeIssuedDto'];

/** The containers a film can be written in, the first the one a render gets when none is chosen. */
export const FLYBY_RENDER_CONTAINERS: readonly FlybyRenderContainer[] = ['mp4', 'webm', 'mkv'];

/** How often a render that is still going is asked about. */
export const FLYBY_RENDER_POLL_MS = 3_000;

/** How often the page asks whether a node is there. A node waiting for work calls every half minute. */
export const FLYBY_RENDER_AVAILABILITY_MS = 30_000;

/** Where a node finds this server: the render-node routes, as the folder address its worker is given. */
export function renderNodeServerUrl(origin: string): string {
  return `${origin.replace(/\/+$/, '')}/api/v1/render-nodes/`;
}

/** Whether a render can still change: queued or running. */
export function flybyRenderIsActive(render: Pick<FlybyRender, 'status'>): boolean {
  return render.status === 'queued' || render.status === 'running';
}

/** The route a file of a render's result is downloaded from. */
export function flybyRenderFileUrl(renderId: string, name: string): string {
  return `/api/v1/flybys/blender-renders/${encodeURIComponent(renderId)}/files/${encodeURIComponent(name)}`;
}

/** Whether rendering on the server is offered: a render node called lately. Asked again while on screen. */
export function useFlybyRenderAvailability(enabled = true) {
  return useQuery({
    queryKey: queryKeys.flybyRenderAvailability,
    queryFn: () => unwrap(api.GET('/api/v1/flybys/blender-renders/availability')),
    enabled,
    refetchInterval: FLYBY_RENDER_AVAILABILITY_MS,
  });
}

/** The account's renders still kept, newest first; asked again every few seconds while one is going. */
export function useFlybyRenders(enabled = true) {
  return useQuery({
    queryKey: queryKeys.flybyRenders,
    queryFn: () => unwrap(api.GET('/api/v1/flybys/blender-renders')),
    enabled,
    refetchInterval: (query) => (query.state.data?.some(flybyRenderIsActive) ? FLYBY_RENDER_POLL_MS : false),
  });
}

/** Stops one of the account's renders. */
export function useCancelFlybyRender() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrap(api.POST('/api/v1/flybys/blender-renders/{id}/cancel', { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.flybyRendersAll }),
  });
}

/** The render nodes issued, for an administrator. */
export function useRenderNodes() {
  return useQuery({
    queryKey: queryKeys.renderNodes,
    queryFn: () => unwrap(api.GET('/api/v1/admin/render-nodes')),
    refetchInterval: FLYBY_RENDER_AVAILABILITY_MS,
  });
}

/**
 * Issues a node. The answer carries its token, the only time the token exists anywhere the browser
 * can read it, so it goes back to the caller to show once and is not put in the query cache.
 */
export function useIssueRenderNode() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (name: string) => unwrap(api.POST('/api/v1/admin/render-nodes', { body: { name } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.renderNodes }),
  });
}

/** Withdraws a node; its next call is refused. */
export function useRevokeRenderNode() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) =>
      unwrapVoid(api.DELETE('/api/v1/admin/render-nodes/{id}', { params: { path: { id } } })),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: queryKeys.renderNodes }),
  });
}
