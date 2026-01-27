# Changelog
All notable changes to this project will be documented in this file. See [conventional commits](https://www.conventionalcommits.org/) for commit guidelines.

- - -
## v3.4.2 - 2026-01-27
#### Bug Fixes
- (**loader**) add validation before stripping header bytes - (ac456b9) - Barry Walker

- - -

## v3.4.1 - 2026-01-27
#### Bug Fixes
- (**ci**) update version in csproj during docker build - (b017705) - Barry Walker
- (**version**) correct version to 3.4.0 in csproj - (ebeefab) - Barry Walker
#### Continuous Integration
- trigger docker build for v3.4.0 - (ce8116a) - Barry Walker
- trigger build for v3.4.0 - (106260b) - Barry Walker

- - -

## v3.4.0 - 2026-01-27
#### Features
- (**cli**) add test filtering options (--filter, --test, --filter-tag, --exclude-tag, --list) - (07778c4) - Barry Walker
- (**grammar**) add test options (skip, trace, timeout, tags) - (bd9bca8) - Barry Walker
- (**grammar**) add setup block syntax - (b0f3d91) - Barry Walker
- (**grammar**) add memfill and memdump tokens and rules - (da06f99) - Barry Walker
- (**listener**) implement test filtering (--filter, --test, --filter-tag, --exclude-tag, --list) - (8e79b43) - Barry Walker
- (**listener**) implement test options (skip, trace, timeout, tags) - (1f51ab0) - Barry Walker
- (**listener**) implement setup block execution before each test - (62f9868) - Barry Walker
- (**listener**) implement memdump function with hex dump output - (612f267) - Barry Walker
- (**listener**) implement memfill function - (cc91a31) - Barry Walker
- (**processor,listener**) implement execution trace buffering and output - (3dc1451) - Barry Walker
#### Bug Fixes
- (**listener**) add setup guards to register/flag assignments, fix overflow flag - (fd6f2c4) - Barry Walker
- (**listener**) add memory boundary validation to memfill/memdump - (5390d74) - Barry Walker
- (**test**) replace vic.BGCOL0 with vic.SCROLY in test-14 - (c89a719) - Barry Walker
- (**tests**) add FillMemory setup and test methods for grammar tests - (9fafa35) - Barry Walker
#### Documentation
- add DSL enhancements documentation - (6be623b) - Barry Walker
- add DSL enhancements implementation plan - (81e5a1a) - Barry Walker
- add DSL enhancements design plan - (2c7a87f) - Barry Walker
#### Tests
- (**grammar**) add test options grammar test (red test) - (43b9c12) - Barry Walker
- (**grammar**) add test options grammar test (red test) - (9add315) - Barry Walker
- (**grammar**) add setup block grammar test (red test) - (874c756) - Barry Walker
- (**grammar**) add memfill/memdump grammar test (red) - (98b87f4) - Barry Walker

- - -

## v3.3.2 - 2026-01-27
#### Bug Fixes
- (**processor**) remove dead code and fix incorrect comment in Addressing.cs - (3e9a062) - Barry Walker
#### Refactoring
- (**processor**) replace 1084-line switch with registry lookup - (dff04eb) - Barry Walker
- (**processor**) add opcode registry with all 151 opcodes - (4b3220d) - Barry Walker
- (**processor**) add opcode handler infrastructure - (f23c391) - Barry Walker
- (**processor**) rename main file to Processor.Execution.cs - (e305952) - Barry Walker
- (**processor**) extract disassembly to Processor.Disassembly.cs - (b47751f) - Barry Walker
- (**processor**) extract CPU operations to Processor.Operations.cs - (443c879) - Barry Walker
- (**processor**) extract addressing modes to Processor.Addressing.cs - (be39417) - Barry Walker
- (**processor**) extract memory operations to Processor.Memory.cs - (ece3e1c) - Barry Walker
- (**processor**) extract core state to Processor.Core.cs - (eefde50) - Barry Walker
#### Chores
- (**tests**) mark Klaus Dormann test as slow category - (ae2321f) - Barry Walker

- - -

## v3.3.1 - 2026-01-27
#### Bug Fixes
- (**grammar**) correct operator precedence and improve portability - (e5c2036) - Barry Walker

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