using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Vml = DocumentFormat.OpenXml.Vml;

namespace OfficeCopilot.OpenXml.Word
{
    /// <summary>Options for a text watermark.</summary>
    public sealed class WatermarkOptions
    {
        public string Text { get; set; } = "DRAFT";
        public string Color { get; set; } = "silver";
        public string Font { get; set; } = "Calibri";
        public string Size { get; set; } = "1pt";
        public string Rotation { get; set; } = "315";
        public string Opacity { get; set; } = ".5";
        public string Width { get; set; } = "415pt";
        public string Height { get; set; } = "207.5pt";
    }

    /// <summary>
    /// Adds / removes a classic VML text watermark. Ported from officecli
    /// WordHandler.AddWatermark + RemoveWatermarkHeaders (C# 7.3 adaptation).
    /// </summary>
    internal static class WordWatermark
    {
        public static void AddWatermark(WordprocessingDocument doc, WatermarkOptions options)
        {
            if (options == null)
                throw new ArgumentNullException(nameof(options));

            // Normalize inputs (mirrors officecli AddWatermark): color hex
            // normalization, rotation 0-360, and a pt suffix on the font size.
            options.Text = string.IsNullOrEmpty(options.Text) ? "DRAFT" : options.Text;
            options.Color = SanitizeWatermarkColor(options.Color);
            options.Font = string.IsNullOrEmpty(options.Font) ? "Calibri" : options.Font;
            options.Size = EnsurePtSuffix(options.Size);
            options.Rotation = NormalizeRotation(options.Rotation);
            options.Opacity = string.IsNullOrEmpty(options.Opacity) ? ".5" : options.Opacity;
            options.Width = string.IsNullOrEmpty(options.Width) ? "415pt" : options.Width;
            options.Height = string.IsNullOrEmpty(options.Height) ? "207.5pt" : options.Height;

            var mainPart = doc.MainDocumentPart;
            if (mainPart == null || mainPart.Document == null)
                throw new InvalidOperationException("Main document part is missing.");

            // Remove any existing watermark first (idempotent).
            RemoveWatermark(doc);

            var headerTypes = new[]
            {
                HeaderFooterValues.Default,
                HeaderFooterValues.First,
                HeaderFooterValues.Even
            };

            for (int i = 0; i < 3; i++)
            {
                var headerPart = mainPart.AddNewPart<HeaderPart>();
                var idx = i + 1;

                var vmlXml = BuildVmlShapeXml(idx, options);
                var headerXml = BuildHeaderXml(idx, vmlXml);

                using (var stream = headerPart.GetStream(FileMode.Create))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    writer.Write(headerXml);
                }

                var body = mainPart.Document.Body;
                var sectPr = body.Elements<SectionProperties>().LastOrDefault()
                    ?? body.AppendChild(new SectionProperties());

                var existingRef = sectPr.Elements<HeaderReference>()
                    .FirstOrDefault(r => r.Type != null && r.Type.Value == headerTypes[i]);
                if (existingRef != null)
                    existingRef.Remove();

                sectPr.PrependChild(new HeaderReference
                {
                    Id = mainPart.GetIdOfPart(headerPart),
                    Type = headerTypes[i]
                });
            }

            // Enable even/odd headers and title page so first/even headers render.
            var settingsPart = mainPart.DocumentSettingsPart ?? mainPart.AddNewPart<DocumentSettingsPart>();
            if (settingsPart.Settings == null)
                settingsPart.Settings = new Settings();
            if (settingsPart.Settings.GetFirstChild<EvenAndOddHeaders>() == null)
                settingsPart.Settings.AddChild(new EvenAndOddHeaders(), false);

            var bodyForTitle = mainPart.Document.Body;
            var titleSectPr = bodyForTitle.Elements<SectionProperties>().LastOrDefault()
                ?? bodyForTitle.AppendChild(new SectionProperties());
            if (titleSectPr.GetFirstChild<TitlePage>() == null)
                titleSectPr.AddChild(new TitlePage(), false);
        }

        public static void RemoveWatermark(WordprocessingDocument doc)
        {
            var mainPart = doc.MainDocumentPart;
            if (mainPart == null)
                return;

            var toRemove = new List<HeaderPart>();
            foreach (var headerPart in mainPart.HeaderParts)
            {
                var header = headerPart.Header;
                if (header == null)
                    continue;

                bool isWatermark = header.Descendants<SdtProperties>().Any(sp =>
                    sp.Descendants<DocPartGallery>().Any(g =>
                        g.Val != null && g.Val.Value != null &&
                        g.Val.Value.Equals("Watermarks", StringComparison.OrdinalIgnoreCase)));

                if (!isWatermark)
                {
                    foreach (var pict in header.Descendants<Picture>())
                    {
                        if (pict.InnerXml.IndexOf("WaterMark", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            isWatermark = true;
                            break;
                        }
                    }
                }

                if (isWatermark)
                    toRemove.Add(headerPart);
            }

            foreach (var headerPart in toRemove)
            {
                var relId = mainPart.GetIdOfPart(headerPart);
                var body = mainPart.Document != null ? mainPart.Document.Body : null;
                if (body != null)
                {
                    foreach (var sectPr in body.Descendants<SectionProperties>())
                    {
                        var refs = sectPr.Elements<HeaderReference>()
                            .Where(r => r.Id != null && r.Id.Value == relId)
                            .ToList();
                        foreach (var r in refs)
                            r.Remove();
                    }
                }
                mainPart.DeletePart(headerPart);
            }
        }

        public static string FindWatermark(WordprocessingDocument doc)
        {
            var mainPart = doc.MainDocumentPart;
            if (mainPart == null)
                return null;

            foreach (var headerPart in mainPart.HeaderParts)
            {
                var header = headerPart.Header;
                if (header == null)
                    continue;

                foreach (var pict in header.Descendants<Picture>())
                {
                    foreach (var shape in pict.Descendants<Vml.Shape>())
                    {
                        var id = shape.Id != null ? shape.Id.Value : null;
                        if (id != null && id.IndexOf("WaterMark", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var textPath = shape.Descendants<Vml.TextPath>().FirstOrDefault();
                            return textPath != null && textPath.String != null
                                ? textPath.String.Value
                                : "(image watermark)";
                        }
                    }
                }
            }

            return null;
        }

        private static string BuildVmlShapeXml(int idx, WatermarkOptions options)
        {
            return $@"<v:shapetype id=""_x0000_t136"" coordsize=""1600,21600"" o:spt=""136"" adj=""10800"" path=""m@7,0l@8,0m@5,21600l@6,21600e"" xmlns:v=""urn:schemas-microsoft-com:vml"" xmlns:o=""urn:schemas-microsoft-com:office:office"">
  <v:formulas>
    <v:f eqn=""sum #0 0 10800""/><v:f eqn=""prod #0 2 1""/><v:f eqn=""sum 21600 0 @1""/>
    <v:f eqn=""sum 0 0 @2""/><v:f eqn=""sum 21600 0 @3""/><v:f eqn=""if @0 @3 0""/>
    <v:f eqn=""if @0 21600 @1""/><v:f eqn=""if @0 0 @2""/><v:f eqn=""if @0 @4 21600""/>
    <v:f eqn=""mid @5 @6""/><v:f eqn=""mid @8 @5""/><v:f eqn=""mid @7 @8""/>
    <v:f eqn=""mid @6 @7""/><v:f eqn=""sum @6 0 @5""/>
  </v:formulas>
  <v:path textpathok=""t"" o:connecttype=""custom"" o:connectlocs=""@9,0;@10,10800;@11,21600;@12,10800"" o:connectangles=""270,180,90,0""/>
  <v:textpath on=""t"" fitshape=""t""/>
  <v:handles><v:h position=""#0,bottomRight"" xrange=""6629,14971""/></v:handles>
  <o:lock v:ext=""edit"" text=""t"" shapetype=""t""/>
</v:shapetype>
<v:shape id=""PowerPlusWaterMarkObject{idx}"" o:spid=""_x0000_s102{4 + idx}"" type=""#_x0000_t136"" style=""position:absolute;margin-left:0;margin-top:0;width:{options.Width};height:{options.Height};rotation:{options.Rotation};z-index:-251654144;mso-wrap-edited:f;mso-position-horizontal:center;mso-position-horizontal-relative:margin;mso-position-vertical:center;mso-position-vertical-relative:margin"" o:allowincell=""f"" fillcolor=""{XmlEscape(options.Color)}"" stroked=""f"" xmlns:v=""urn:schemas-microsoft-com:vml"" xmlns:o=""urn:schemas-microsoft-com:office:office"">
  <v:fill opacity=""{options.Opacity}""/>
  <v:textpath style=""font-family:&quot;{XmlEscape(options.Font)}&quot;;font-size:{options.Size}"" string=""{XmlEscape(options.Text)}""/>
</v:shape>";
        }

        private static string BuildHeaderXml(int idx, string vmlXml)
        {
            return $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<w:hdr xmlns:w=""http://schemas.openxmlformats.org/wordprocessingml/2006/main""
       xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships""
       xmlns:w10=""urn:schemas-microsoft-com:office:word"">
  <w:sdt>
    <w:sdtPr>
      <w:id w:val=""{-1000 - idx}""/>
      <w:docPartObj>
        <w:docPartGallery w:val=""Watermarks""/>
        <w:docPartUnique/>
      </w:docPartObj>
    </w:sdtPr>
    <w:sdtContent>
      <w:p>
        <w:pPr><w:pStyle w:val=""Header""/></w:pPr>
        <w:r>
          <w:rPr><w:noProof/></w:rPr>
          <w:pict>{vmlXml}</w:pict>
        </w:r>
      </w:p>
    </w:sdtContent>
  </w:sdt>
</w:hdr>";
        }

        internal static string SanitizeWatermarkColor(string value)
        {
            var input = (value ?? string.Empty).Trim();
            if (input.Length == 0)
                return "silver";

            var hadHash = input[0] == '#';
            var hex = hadHash ? input.Substring(1) : input;
            hex = hex.ToUpperInvariant();

            if (hex.Length > 0 && IsHex(hex))
            {
                switch (hex.Length)
                {
                    case 3:
                        return string.Concat(hex[0], hex[0], hex[1], hex[1], hex[2], hex[2]);
                    case 6:
                        return hex;
                    case 8:
                        // #RRGGBBAA → strip trailing alpha; bare AARRGGBB → strip leading alpha.
                        return hadHash ? hex.Substring(0, 6) : hex.Substring(2);
                }
            }

            // Named colors (silver, red, ...) and rgb()/hsl() pass through — VML fillcolor accepts them.
            return input;
        }

        internal static string NormalizeRotation(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "315";

            double degrees;
            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out degrees))
                throw new ArgumentException(
                    string.Format("Invalid 'rotation' value: '{0}'. Expected a number in degrees (e.g. 315 or -45).", value));

            degrees %= 360;
            if (degrees < 0)
                degrees += 360;
            return degrees.ToString("0.##", CultureInfo.InvariantCulture);
        }

        private static string EnsurePtSuffix(string value)
        {
            var size = string.IsNullOrWhiteSpace(value) ? "1pt" : value.Trim();
            return size.EndsWith("pt", StringComparison.OrdinalIgnoreCase) ? size : size + "pt";
        }

        private static bool IsHex(string s)
        {
            foreach (var c in s)
            {
                if (!((c >= '0' && c <= '9') || (c >= 'A' && c <= 'F')))
                    return false;
            }
            return true;
        }

        private static string XmlEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return value;
            return value
                .Replace("&", "&amp;")
                .Replace("\"", "&quot;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
        }
    }
}
