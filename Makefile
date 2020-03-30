grammar:
	cd sim6502/Grammar && \
	java -jar ../../dependencies/antlr-4.8-complete.jar -Dlanguage=CSharp -listener \
		-o Generated -package sim6502.Grammar.Generated sim6502.g4 && \
	cd ../..

build: grammar
	docker build . -t barrywalker71/sim6502cli:latest

push: build
	docker push barrywalker71/sim6502cli:latest
