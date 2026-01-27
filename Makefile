.PHONY: grammar build test clean

grammar:
	cd sim6502/Grammar && \
	java -jar ../../dependencies/antlr-4.13.1-complete.jar -Dlanguage=CSharp -listener \
		-o Generated -package sim6502.Grammar.Generated sim6502.g4 && \
	cd ../..

build: grammar
	dotnet build -c Release

test:
	dotnet test -c Release

clean:
	dotnet clean
	rm -rf sim6502/bin sim6502/obj sim6502tests/bin sim6502tests/obj

# Setup git hooks for conventional commits
setup-hooks:
	git config core.hooksPath .githooks
	@echo "Git hooks configured to use .githooks directory"
