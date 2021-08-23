using APSIM.Services.Documentation;
using System;
using System.Collections.Generic;
using APSIM.Shared.Utilities;
using APSIM.Interop.Documentation.Helpers;
using APSIM.Interop.Documentation.Renderers;
using System.Linq;
using APSIM.Interop.Markdown.Renderers;
#if NETCOREAPP
using MigraDocCore.DocumentObjectModel;
using MigraDocCore.DocumentObjectModel.MigraDoc.DocumentObjectModel.Shapes;
using MigraDocCore.Rendering;
using PdfSharpCore.Fonts;
using SixLabors.ImageSharp.PixelFormats;
#else
using MigraDoc.DocumentObjectModel;
using MigraDoc.Rendering;
using PdfSharp.Fonts;
#endif

namespace APSIM.Interop.Documentation
{
    /// <summary>
    /// This class will generate a PDF file from a collection of tags.
    /// </summary>
    public class PdfWriter
    {
        /// <summary>
        /// PDF generation options.
        /// </summary>
        private PdfOptions options;

        /// <summary>
        /// Construct a <see cref="PdfWriter" /> instance with default settings.
        /// </summary>
        public PdfWriter()
        {
            options = PdfOptions.Default;
        }

        /// <summary>
        /// Construct a <see cref="PdfWriter" /> instance with a custom image search path.
        /// </summary>
        /// <param name="options">PDF generation options.</param>
        public PdfWriter(PdfOptions options)
        {
            this.options = options;
        }

        /// <summary>
        /// Convert a given list of tags into a PDF document and
        /// save the document to the given path.
        /// </summary>
        /// <param name="fileName">File name of the generated pdf.</param>
        /// <param name="tags">Tags to be converted to a PDF.</param>
        public void Write(string fileName, IEnumerable<ITag> tags)
        {
            // This is a bit tricky on non-Windows platforms. 
            // Normally PdfSharp tries to get a Windows DC for associated font information
            // See https://alex-maz.info/pdfsharp_150 for the work-around we can apply here.
            // See also http://stackoverflow.com/questions/32726223/pdfsharp-migradoc-font-resolver-for-embedded-fonts-system-argumentexception
            // The work-around is to register our own fontresolver. We don't need to do this on Windows.
            if (!ProcessUtilities.CurrentOS.IsWindows && !(GlobalFontSettings.FontResolver is FontResolver))
                GlobalFontSettings.FontResolver = new FontResolver();

            Document pdf = CreateStandardDocument();
            PdfBuilder pdfRenderer = new PdfBuilder(pdf, options);

            foreach (ITag tag in tags)
                pdfRenderer.Write(tag);

            PdfDocumentRenderer renderer = new PdfDocumentRenderer(false);
            renderer.Document = pdf;
            renderer.RenderDocument();
            renderer.Save(fileName);
        }

        private static Document CreateStandardDocument()
        {
            Document document = new Document();

            document.DefaultPageSetup.LeftMargin = Unit.FromCentimeter(1);
            document.DefaultPageSetup.RightMargin = Unit.FromCentimeter(1);
            document.DefaultPageSetup.TopMargin = Unit.FromCentimeter(1);
            document.DefaultPageSetup.BottomMargin = Unit.FromCentimeter(1);
            document.Styles.Normal.ParagraphFormat.SpaceAfter = Unit.FromPoint(10);

            document.AddSection().AddParagraph();
            return document;
        }
    }
}
