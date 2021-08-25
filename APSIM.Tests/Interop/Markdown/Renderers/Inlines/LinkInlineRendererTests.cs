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
using Markdig.Parsers.Inlines;
using System;
using System.Drawing;
using System.IO;

namespace APSIM.Tests.Interop.Markdown.Renderers.Inlines
{
    /// <summary>
    /// Tests for <see cref="LinkInlineRenderer"/>.
    /// </summary>
    [TestFixture]
    public class LinkInlineRendererTests
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
        /// The <see cref="LinkInlineRenderer"/> instance being tested.
        /// </summary>
        private LinkInlineRenderer renderer;

        /// <summary>
        /// Sample link inline which may be used by tests.
        /// </summary>
        private LinkInline inline;

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
            renderer = new LinkInlineRenderer(null);
            inline = new LinkInline();
            inline.Url = "sample link";
        }

        /// <summary>
        /// Ensure that the link's dynamic URI is used if one is provided.
        /// </summary>
        [Test]
        public void TestDynamicUri()
        {
            string dynamicUri = "dynamic uri";
            inline.GetDynamicUrl = () => dynamicUri;
            Mock<PdfBuilder> builder = new Mock<PdfBuilder>(document, PdfOptions.Default);
            builder.Setup(b => b.SetLinkState(It.IsAny<string>()))
                   .Callback<string>(uri => Assert.AreEqual(dynamicUri, uri))
                   .CallBase();
            renderer.Write(builder.Object, inline);
        }

        /// <summary>
        /// Ensure that the link's Url property is used if no dynamic uri
        /// is provided.
        /// </summary>
        [Test]
        public void TestStaticUri()
        {
            inline.GetDynamicUrl = null;
            Mock<PdfBuilder> builder = new Mock<PdfBuilder>(document, PdfOptions.Default);
            builder.Setup(b => b.SetLinkState(It.IsAny<string>()))
                   .Callback<string>(uri => Assert.AreEqual(inline.Url, uri))
                   .CallBase();
            renderer.Write(builder.Object, inline);
        }

        /// <summary>
        /// Ensure that an image link's dynamic URI is used if one is provided.
        /// </summary>
        [Test]
        public void TestDynamicImageUri()
        {
            string dynamicUri = "dynamic uri";
            inline.GetDynamicUrl = () => dynamicUri;
            inline.IsImage = true;
            using (Image image = new Bitmap(1, 1))
            {
                Mock<LinkInlineRenderer> renderer = new Mock<LinkInlineRenderer>(null);
                renderer.CallBase = true;
                renderer.Setup(b => b.GetImage(It.IsAny<string>()))
                        .Callback<string>(uri => Assert.AreEqual(dynamicUri, uri))
                        .Returns(image);
                renderer.Object.Write(pdfBuilder, inline);
            }
        }

        /// <summary>
        /// Ensure that an image link's Url property is used if no dynamic uri
        /// is provided.
        /// </summary>
        [Test]
        public void TestStaticImageUri()
        {
            inline.GetDynamicUrl = null;
            inline.IsImage = true;
            Mock<LinkInlineRenderer> renderer = new Mock<LinkInlineRenderer>(null);
            renderer.CallBase = true;
            using (Image image = new Bitmap(1, 1))
            {
                renderer.Setup(b => b.GetImage(It.IsAny<string>()))
                        .Callback<string>(uri => Assert.AreEqual(inline.Url, uri))
                        .Returns(image);
                renderer.Object.Write(pdfBuilder, inline);
            }
        }

        /// <summary>
        /// Ensure children of a non-image link are written to the document.
        /// </summary>
        /// <remarks>
        /// Really need to mock out pdfbuilder/migradoc...
        /// </remarks>
        [Test]
        public void EnsureChildrenAreWritten()
        {
            string text = "link description/title";
            inline.AppendChild(new LiteralInline(text));
            renderer.Write(pdfBuilder, inline);

            Assert.AreEqual(1, document.LastSection.Elements.Count);
            Paragraph paragraph = (Paragraph)document.LastSection.Elements[0];
            Assert.AreEqual(1, paragraph.Elements.Count);
            Hyperlink hyperlink = (Hyperlink)paragraph.Elements[0];
            Assert.AreEqual(1, hyperlink.Elements.Count);
            FormattedText formatted = (FormattedText)hyperlink.Elements[0];
            Assert.AreEqual(1, formatted.Elements.Count);
            Text plainText = (Text)formatted.Elements[0];
            Assert.AreEqual(text, plainText.Content);
        }

        /// <summary>
        /// Ensure that any subsequent additions are written into the same
        /// paragraph.
        /// </summary>
        [Test]
        public void EnsureSubsequentAdditionsInSameParagraph()
        {
            renderer.Write(pdfBuilder, inline);
            pdfBuilder.AppendText("extra content", TextStyle.Normal);
            Assert.AreEqual(1, document.LastSection.Elements.Count);
        }

        /// <summary>
        /// Ensure that subsequent additions don't have hyperlink style.
        /// </summary>
        [Test]
        public void EnsureSubsequentAdditionsNotInHyperlink()
        {
            renderer.Write(pdfBuilder, inline);
            pdfBuilder.AppendText("extra content", TextStyle.Normal);
            Paragraph paragraph = (Paragraph)document.LastSection.Elements[0];
            Assert.AreEqual(2, paragraph.Elements.Count);
        }

        /// <summary>
        /// Ensure that an image link to a local file is resolved correctly.
        /// </summary>
        [Test]
        public void TestImageFromLocalFile()
        {
            string fileName = Path.GetTempFileName();
            Image image = new Bitmap(4, 4);
            image.Save(fileName);
            try
            {
                string imageName = Path.GetFileName(fileName);
                string filePath = Path.GetDirectoryName(fileName);
                renderer = new LinkInlineRenderer(filePath);
                inline.IsImage = true;

                Assert.DoesNotThrow(() => renderer.GetImage(imageName).Dispose());
            }
            finally
            {
                File.Delete(fileName);
            }
        }

        /// <summary>
        /// Ensure that an image uri to an embedded resource is resolved correctly.
        /// </summary>
        /// <param name="resourceName">Resource name to try.</param>
        /// <remarks>
        /// todo: test loading of images from other assemblies. This requires
        /// the assembly to be loaded into the app domain. This should be done
        /// once these tests are moved into UnitTests project.
        /// </remarks>
        [TestCase("APSIM.Interop.Resources.Images.AIBanner.png")]
        public void TestImageFromResource(string resourceName)
        {
            Assert.DoesNotThrow(() => renderer.GetImage(resourceName).Dispose());
        }

        /// <summary>
        /// Ensure that an image link with an invalid uri triggers an exception.
        /// </summary>
        [Test]
        public void EnsureInvalidImageUriThrows()
        {
            string imageName = Guid.NewGuid().ToString();
            Assert.Throws<FileNotFoundException>(() => renderer.GetImage(imageName));
        }

        /// <summary>
        /// Ensure that any children of an image link are written to a
        /// new paragraph.
        /// </summary>
        [Test]
        public void EnsureContentsAfterImageNotInSameParagraph()
        {
            inline.IsImage = true;
            inline.AppendChild(new LiteralInline("caption"));
            using (Image image = new Bitmap(4, 4))
            {
                Mock<LinkInlineRenderer> renderer = new Mock<LinkInlineRenderer>(null);
                renderer.Setup(r => r.GetImage(It.IsAny<string>())).Returns(image);
                renderer.CallBase = true;
                renderer.Object.Write(pdfBuilder, inline);
            }
            pdfBuilder.AppendText("Not be in same paragraph as image", TextStyle.Normal);
            Assert.AreEqual(2, document.LastSection.Elements.Count);
        }

        /// <summary>
        /// Ensure that an image is inserted into an existing paragraph
        /// (if one exists).
        /// </summary>
        [Test]
        public void EnsureImageGoesInExistingParagraph()
        {
            pdfBuilder.AppendText("Not be in same paragraph as image", TextStyle.Normal);
            inline.IsImage = true;
            using (Image image = new Bitmap(4, 4))
            {
                Mock<LinkInlineRenderer> renderer = new Mock<LinkInlineRenderer>(null);
                renderer.CallBase = true;
                renderer.Setup(r => r.GetImage(It.IsAny<string>())).Returns(image);
                renderer.Object.Write(pdfBuilder, inline);
            }
            Assert.AreEqual(1, document.LastSection.Elements.Count);
        }
    }
}
