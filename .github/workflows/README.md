# Workflows

[Up to automation](../README.md) | [Monorepo](../../README.md)

## Labyrinth Steam Deployment

[`labyrinth-steam-deploy.yml`](labyrinth-steam-deploy.yml) runs for tags matching `v*.*.*`:

1. Check out the monorepo with Git LFS.
2. Restore the nested Unity project's `Library` cache.
3. Build `EFYV-labyrinth` for `StandaloneWindows64` with GameCI.
4. Transfer the build artifact between jobs.
5. Publish it through SteamPipe.

Before enabling publication, replace the placeholder Steam application ID and configure the Unity and Steam secrets referenced by the workflow.
