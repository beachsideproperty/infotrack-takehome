export const API_URL = (import.meta.env.VITE_API_URL ?? 'http://localhost:8080').replace(/\/+$/, '')

export interface Health {
  status: string
  version: string
}

export async function getHealth(signal?: AbortSignal): Promise<Health> {
  const res = await fetch(`${API_URL}/health`, { signal })
  if (!res.ok) throw new Error(`Health check failed: ${res.status}`)
  return res.json() as Promise<Health>
}
