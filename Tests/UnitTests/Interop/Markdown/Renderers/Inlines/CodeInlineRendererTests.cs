using NUnit.Framework;
using APSIM.Interop.Documentation;
using APSIM.Interop.Markdown.Renderers;
using APSIM.Interop.Markdown.Renderers.Blocks;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using APSIM.Interop.Documentation.Extensions;
using APSIM.Interop.Markdown;
using MigraDocCore.DocumentObjectModel;
using APSIM.Interop.Markdown.Renderers.Inlines;
using Moq;

namespace UnitTests.Interop.Markdown.Renderers.Inlines
{
    /// <summary>
    /// Tests for <see cref="CodeInlineRenderer"/>.
    /// </summary>
    [TestFixture]
    public class CodeInlineRendererTests
    {
        /// <summary>
        /// PDF Builder API instance.
        /// </summary>
        private PdfBuilder pdfBuilder;

        /// <summary>
        /// MigraDoc document to which the renderer will write.
        /// </summary>
        private Document document;

        /// <summary>
        /// The <see cref="CodeInlineRenderer"/> instance being tested.
        /// </summary>
        private CodeInlineRenderer renderer;

        /// <summary>
        /// Sample code inline which may be used by tests.
        /// </summary>
        private CodeInline inline;

        /// <summary>
        /// Text in the sample code inline.
        /// </summary>
        private string sampleCode;

        /// <summary>
        /// Initialise the testing environment.
        /// </summary>
        [SetUp]
        public void Setup()
        {
            document = new Document();
            // Workaround for a quirk in the migradoc API.
            _ = document.AddSection().Elements;
            pdfBuilder = new PdfBuilder(document, PdfOptions.Default);
            renderer = new CodeInlineRenderer();
            inline = new CodeInline();
            inline.Content = sampleCode = "sample code";
        }

        /// <summary>
        /// Ensure that the code text is inserted.
        /// </summary>
        [Test]
        public void EnsureCodeTextIsInserted()
        {
            renderer.Write(pdfBuilder, inline);
            Assert.AreEqual(1, document.LastSection.Elements.Count);
            Paragraph paragraph = (Paragraph)document.LastSection.Elements[0];
            Assert.AreEqual(1, paragraph.Elements.Count);
            FormattedText text = (FormattedText)paragraph.Elements[0];
            Assert.AreEqual(1, text.Elements.Count);
            Text rawText = (Text)text.Elements[0];
            Assert.AreEqual(sampleCode, rawText.Content);
        }

        /// <summary>
        /// Ensure that inserted code has appropriate style applied.
        /// </summary>
        [Test]
        public void EnsureCodeStyleIsApplied()
        {
            Mock<PdfBuilder> mockBuilder = new Mock<PdfBuilder>(document, PdfOptions.Default);
            mockBuilder.Setup(b => b.AppendText(It.IsAny<string>(), It.IsAny<TextStyle>()))
                       .Callback<string, TextStyle>((_, style) => Assert.AreEqual(TextStyle.Code, style))
                       .CallBase();
            renderer.Write(mockBuilder.Object, inline);
        }

        /// <summary>
        /// Ensure that any subsequent additions go into the same paragraph.
        /// </summary>
        [Test]
        public void EnsureSubsequentAdditionsInSameParagraph()
        {
            renderer.Write(pdfBuilder, inline);
            pdfBuilder.AppendText("subsequent addition", TextStyle.Normal);
            Assert.AreEqual(1, document.LastSection.Elements.Count);
        }

        /// <summary>
        /// Ensure that subsequent additions are not included in the code element.
        /// </summary>
        [Test]
        public void EnsureSubsequentAdditionsNotInCode()
        {
            renderer.Write(pdfBuilder, inline);
            pdfBuilder.AppendText("this was inserted after the code inline", TextStyle.Normal);
            Assert.AreEqual(1, document.LastSection.Elements.Count);
            Paragraph paragraph = (Paragraph)document.LastSection.Elements[0];
            Assert.AreEqual(2, paragraph.Elements.Count);
        }

        /// <summary>
        /// Ensure that the code contents are written to an existing
        /// paragraph (if there is one).
        /// </summary>
        [Test]
        public void EnsureContentGoesInExistingParagraph()
        {
            pdfBuilder.AppendText("pre-existing material", TextStyle.Normal);
            renderer.Write(pdfBuilder, inline);
            Assert.AreEqual(1, document.LastSection.Elements.Count);
            Paragraph paragraph = (Paragraph)document.LastSection.Elements[0];
            Assert.AreEqual(2, paragraph.Elements.Count);
        }
    }
}
