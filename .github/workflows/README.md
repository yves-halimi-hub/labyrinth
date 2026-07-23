# Workflows

[Up to automation](../README.md) | [Monorepo](../../README.md)

## Continuous Integration

[`ci.yml`](ci.yml) runs on every push and pull request:

1. Check out the monorepo.
2. Check out the private [`yves-halimi-hub/EFYV-runtime-kernel`](https://github.com/yves-halimi-hub/EFYV-runtime-kernel) dependency at immutable commit `16260b07de77e15d09f373d5833a5e7243fe14eb` into `.deps/EFYV-runtime-kernel`, without persisting Git credentials.
3. Install the .NET 8 and .NET 10 SDKs (the backend and LabyMake engine target `net8.0`; the game verification project targets `net10.0`) plus Python 3.
4. Build the pinned kernel as a Release x64 shared library with the runner's hosted MSVC generator, add the directory containing `efyv_runtime_kernel.dll` to `GITHUB_PATH`, and set `EFYV_REQUIRE_NATIVE_KERNEL=1`. The full backend verification therefore fails if the native ABI cannot load and compares its RGBA/CRC behavior against the managed implementation when it can.
5. Run the backend verification project, build the stateless `EFYV-labymake/services/labymake-engine` gRPC service,
   run the game/editor verification project, and execute the game Python contract tests.

The job runs on `windows-latest` by necessity, not preference: `SafePathPolicy.IsSafeFileStem` delegates to `Path.GetInvalidFileNameChars()`, and the backend suite asserts Windows-only rejections (`a|b`, tab, backslash) that Linux treats as valid file-name characters.

The repository must define `EFYV_RUNTIME_KERNEL_DEPLOY_KEY` as a GitHub Actions secret containing a read-only SSH deploy key authorized for the private runtime-kernel repository. GitHub does not expose repository secrets to workflows triggered by pull requests from forks, so those runs cannot check out the private dependency and will fail before verification.

## Labyrinth Steam Deployment

[`labyrinth-steam-deploy.yml`](labyrinth-steam-deploy.yml) runs for tags matching `v*.*.*`:

1. Check out the monorepo (no LFS - the repository stores no LFS content).
2. Restore the nested Unity project's `Library` cache (keyed over `Assets/`, `Packages/`, `ProjectSettings/`, and the backend UPM source at `EFYV-labybackend/Core/`, which the project consumes as a relative `file:` package).
3. Build `EFYV-labyrinth` for `StandaloneWindows64` with GameCI `unity-builder@v4` (`unityVersion: auto` reads `ProjectSettings/ProjectVersion.txt`).
4. Transfer the build artifact between jobs with `upload/download-artifact@v4`.
5. Publish through SteamPipe (`steam-deploy@v3`, `configVdf` authentication). The deploy job is skipped - without failing the build - until the `STEAM_APP_ID` repository **variable** is set.

Required configuration before first publication: repository variable `STEAM_APP_ID`; secrets `UNITY_LICENSE` (or `UNITY_EMAIL`+`UNITY_PASSWORD`), `STEAM_USERNAME`, and `STEAM_CONFIG_VDF` (a SteamPipe `config.vdf` captured after a `steamcmd` login, per the steam-deploy docs).

Known limitation: the project pins a **beta** editor (`6000.6.0b4`), and GameCI publishes docker images for released editors only. Until the pin moves to a released version, trigger the workflow manually (`workflow_dispatch`) and pass a released `unityVersion` (e.g. a 6000.3 LTS patch), or expect the auto-resolved image lookup to fail.
