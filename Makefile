.PHONY: grammar build test clean publish publish-all publish-clean differential

# -visitor is required: sim6502-lsp/Server/DiagnosticsProvider.cs derives from
# sim6502BaseVisitor, and without the flag ANTLR does not emit it.
# No -package: the namespace comes from the @header block in sim6502.g4, and
# passing both emits it twice, which does not compile.
grammar:
	cd sim6502/Grammar && \
	java -jar ../../dependencies/antlr-4.13.1-complete.jar -Dlanguage=CSharp -listener -visitor \
		-o Generated sim6502.g4 && \
	cd ../..

build: grammar
	dotnet build -c Release

test:
	dotnet test -c Release

clean:
	dotnet clean
	rm -rf sim6502/bin sim6502/obj sim6502tests/bin sim6502tests/obj

PLATFORMS = linux-x64 linux-arm64 osx-x64 osx-arm64 win-x64 win-arm64

publish-all:
	@for rid in $(PLATFORMS); do \
		echo "Publishing $$rid..."; \
		dotnet publish sim6502/sim6502.csproj -c Release -r $$rid -p:PublishSingleFile=true -o publish/$$rid; \
	done

publish:
ifndef RID
	$(error RID is required. Usage: make publish RID=osx-arm64)
endif
	dotnet publish sim6502/sim6502.csproj -c Release -r $(RID) -p:PublishSingleFile=true -o publish/$(RID)

publish-clean:
	rm -rf publish/

# Setup git hooks for conventional commits
setup-hooks:
	git config core.hooksPath .githooks
	@echo "Git hooks configured to use .githooks directory"

# Differential check: example/ultimate.suite against u64sim and real hardware.
# Requires a physical Ultimate 64. Never run in CI.
differential:
	@./scripts/differential.sh
