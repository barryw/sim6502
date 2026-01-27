# Changelog
All notable changes to this project will be documented in this file. See [conventional commits](https://www.conventionalcommits.org/) for commit guidelines.

- - -
## v3.3.0 - 2026-01-27
#### Features
- add stop_on_address tests and comprehensive DSL documentation - (fec8fa4) - Barry Walker

- - -

## v3.2.2 - 2026-01-27
#### Bug Fixes
- use GitHub API to get version in kaniko step - (dec200b) - Barry Walker

- - -

## v3.2.1 - 2026-01-27
#### Bug Fixes
- use alpine/git for docker step, remove tests from Dockerfile - (1b036ee) - Barry Walker

- - -

## v3.2.0 - 2026-01-27
#### Features
- add kaniko docker build step - (0f926d9) - Barry Walker

- - -

## v3.1.8 - 2026-01-27
#### Bug Fixes
- remove docker step (needs privileged mode) - (0577394) - Barry Walker
- use woodpecker docker-buildx plugin - (16cbc22) - Barry Walker

- - -

## v3.1.7 - 2026-01-27
#### Bug Fixes
- simplify docker step to just build latest tag - (e3de947) - Barry Walker

- - -

## v3.1.6 - 2026-01-27
#### Bug Fixes
- use /woodpecker/src for version file between steps - (0b95505) - Barry Walker

- - -

## v3.1.5 - 2026-01-27
#### Bug Fixes
- fetch docker version from GitHub releases API - (ff2d32b) - Barry Walker

- - -

## v3.1.4 - 2026-01-27
#### Bug Fixes
- extract docker version from csproj instead of temp file - (11bebc9) - Barry Walker

- - -

## v3.1.3 - 2026-01-27
#### Bug Fixes
- skip entire pipeline for version commits - (5236566) - Barry Walker

- - -

## v3.1.2 - 2026-01-27
#### Bug Fixes
- use [CI SKIP] for Woodpecker to skip version commit pipelines - (8cf3c36) - Barry Walker
#### Chores
- trigger pipeline - (2be6938) - Barry Walker

- - -

## v3.1.1 - 2026-01-27
#### Bug Fixes
- pass version via file for docker build (kaniko has no git) - (766a4a6) - Barry Walker

- - -

## v3.1.0 - 2026-01-27
#### Features
- add GitHub release creation with commit-based notes - (9658810) - Barry Walker

- - -

## v3.0.2 - 2026-01-27
#### Bug Fixes
- correct evaluate condition to check for chore(version) - (4e30337) - Barry Walker

- - -

## v3.0.1 - 2026-01-27
#### Bug Fixes
- simplify cog commit types and fix tarball extraction - (f9ec7c7) - Barry Walker
#### Refactoring
- (**ci**) simplify - all commits bump patch, use cog properly - (fdc6351) - Barry Walker

- - -

## v0.0.4 - 2026-01-27
#### Bug Fixes
- (**ci**) skip version commits to prevent loop, remove tests from Dockerfile - (7d34334) - Barry Walker
#### Documentation
- trigger pipeline - (0e68e25) - Barry Walker
#### Refactoring
- (**ci**) simplified pipeline based on PaperlessMCP approach - (e33bb7a) - Barry Walker
#### Chores
- (**release**) bump version to 0.0.4 [skip ci] - (b184327) - Woodpecker CI

- - -

## v0.0.3 - 2026-01-27
#### Bug Fixes
- (**ci**) quote commit command to avoid YAML colon parsing - (18da639) - Barry Walker
- (**ci**) remove cog pre_bump_hooks and commit csproj before cog bump - (f81aa7c) - Barry Walker
- (**ci**) use pipe delimiter in sed to avoid slash conflicts - (3aca2c3) - Barry Walker
#### Refactoring
- (**ci**) simplify to single pipeline with clear steps - (1bb3511) - Barry Walker
#### Chores
- (**version**) 0.0.3 - (8182604) - Woodpecker CI
- update version in csproj - (511eb6c) - Woodpecker CI

- - -

## v0.0.2 - 2026-01-27
#### Bug Fixes
- (**ci**) fetch tags before cog bump to detect current version - (429845c) - Barry Walker
- (**ci**) configure all commit types to trigger patch bumps - (ff06d26) - Barry Walker
- (**ci**) trigger docker build on all tags, not just v* prefix - (677b179) - Barry Walker
- (**ci**) use kaniko debug image with shell support - (f0eea09) - Barry Walker
#### Documentation
- add title header to README - (4163324) - Barry Walker
#### Chores
- (**version**) 0.0.2 - (269ad9d) - Woodpecker CI
- (**version**) 0.0.1 - (8491994) - Woodpecker CI
- (**version**) 0.0.1 - (eb307ed) - Woodpecker CI
- (**version**) 0.0.1 - (df92fd0) - Woodpecker CI
- (**version**) 0.0.1 - (61e01de) - Woodpecker CI
- (**version**) 0.0.1 - (b2ff709) - Woodpecker CI

- - -

## v0.0.1 - 2026-01-27
#### Chores
- (**version**) 0.0.1 - (2a0726a) - Woodpecker CI

- - -

## 0.0.3 - 2026-01-27
#### Bug Fixes
- **(ci)** quote commit command to avoid YAML colon parsing - (18da639) - *barryw*
- **(ci)** remove cog pre_bump_hooks and commit csproj before cog bump - (f81aa7c) - *barryw*
- **(ci)** use pipe delimiter in sed to avoid slash conflicts - (3aca2c3) - *barryw*
#### Chores
- update version in csproj - (511eb6c) - Woodpecker CI
#### Refactoring
- **(ci)** simplify to single pipeline with clear steps - (1bb3511) - *barryw*

- - -

## 0.0.2 - 2026-01-27
#### Bug Fixes
- **(ci)** fetch tags before cog bump to detect current version - (429845c) - *barryw*
- **(ci)** configure all commit types to trigger patch bumps - (ff06d26) - *barryw*
- **(ci)** trigger docker build on all tags, not just v* prefix - (677b179) - *barryw*
- **(ci)** use kaniko debug image with shell support - (f0eea09) - *barryw*
#### Chores
- **(version)** 0.0.1 - (8491994) - Woodpecker CI
- **(version)** 0.0.1 - (eb307ed) - Woodpecker CI
- **(version)** 0.0.1 - (df92fd0) - Woodpecker CI
- **(version)** 0.0.1 - (61e01de) - Woodpecker CI
- **(version)** 0.0.1 - (b2ff709) - Woodpecker CI
#### Documentation
- add title header to README - (4163324) - barryw

- - -

## 0.0.1 - 2026-01-27
#### Chores
- **(version)** 0.0.1 - (eb307ed) - Woodpecker CI

- - -

## 0.0.1 - 2026-01-27
#### Chores
- **(version)** 0.0.1 - (df92fd0) - Woodpecker CI

- - -

## 0.0.1 - 2026-01-27
#### Bug Fixes
- **(ci)** configure all commit types to trigger patch bumps - (ff06d26) - *barryw*

- - -

## 0.0.1 - 2026-01-27
#### Bug Fixes
- **(ci)** trigger docker build on all tags, not just v* prefix - (677b179) - *barryw*

- - -

## 0.0.1 - 2026-01-27
#### Bug Fixes
- **(ci)** use kaniko debug image with shell support - (f0eea09) - *barryw*

- - -

## 0.0.1 - 2026-01-27
#### Bug Fixes
- **(ci)** use Linux sed syntax in cog pre_bump_hooks - (d2adbd5) - *barryw*

- - -

Changelog generated by [cocogitto](https://github.com/cocogitto/cocogitto).