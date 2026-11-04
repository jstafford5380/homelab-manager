export async function request(
  path: string,
  body?: unknown,
  method = "POST",
  signal?: AbortSignal,
) {
  const response = await fetch(
    "/api/v1" + path,
    body === undefined
      ? { signal }
      : {
          method,
          signal,
          headers: {
            "Content-Type": "application/json",
            "X-Homelab-Request": "1",
          },
          body: JSON.stringify(body),
        },
  );
  if (!response.ok) {
    const data = await response.json().catch(() => ({}));
    throw new Error(data.error || `Request failed (${response.status})`);
  }
  return response.status === 204 ? null : response.json().catch(() => null);
}
