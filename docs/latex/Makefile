.PHONY: all clean

# Main target
all: sim6502.pdf

# Build PDF from LaTeX source
sim6502.pdf: sim6502.tex
	pdflatex sim6502.tex
	pdflatex sim6502.tex

# Clean build artifacts
clean:
	rm -f *.aux *.log *.out *.toc *.pdf
