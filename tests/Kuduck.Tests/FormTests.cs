using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ImageMagick;
using Kuduck.Tools;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace Kuduck.Tests
{
    /// <summary>Filling in a PDF form, putting a signature on a page, and the searchable text layer.</summary>
    internal static class FormTests
    {
        /// <summary>
        /// A one-page form, written by hand as Acrobat would: a name box, a notes box, a tick box, round options for a size,
        /// a list of cities, and a street box inside an "address" group.
        /// </summary>
        public static string MakeForm(string path)
        {
            var objects = new List<string>
            {
                "<< /Type /Catalog /Pages 2 0 R /AcroForm << /Fields [4 0 R 5 0 R 6 0 R 7 0 R 10 0 R 11 0 R] >> >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 600 800] /Annots [4 0 R 5 0 R 6 0 R 8 0 R 9 0 R 10 0 R 13 0 R] >>",
                "<< /FT /Tx /T (name) /TU (Your full name) /V (Old name) /Type /Annot /Subtype /Widget /Rect [50 700 300 720] /P 3 0 R >>",
                "<< /FT /Tx /T (notes) /Ff 4096 /Type /Annot /Subtype /Widget /Rect [50 600 300 680] /P 3 0 R >>",
                "<< /FT /Btn /T (agree) /V /Off /AS /Off /Type /Annot /Subtype /Widget /Rect [50 560 62 572] /P 3 0 R /AP << /N << /Yes 14 0 R /Off 14 0 R >> >> >>",
                "<< /FT /Btn /Ff 49152 /T (size) /V /Off /Kids [8 0 R 9 0 R] >>",
                "<< /Type /Annot /Subtype /Widget /Parent 7 0 R /Rect [50 520 62 532] /AS /Off /P 3 0 R /AP << /N << /Small 14 0 R /Off 14 0 R >> >> >>",
                "<< /Type /Annot /Subtype /Widget /Parent 7 0 R /Rect [80 520 92 532] /AS /Off /P 3 0 R /AP << /N << /Large 14 0 R /Off 14 0 R >> >> >>",
                "<< /FT /Ch /Ff 131072 /T (city) /Opt [(Kochi) [(DL) (Delhi)] (Mumbai)] /V (Kochi) /Type /Annot /Subtype /Widget /Rect [50 480 200 500] /P 3 0 R >>",
                "<< /T (address) /Kids [12 0 R] >>",
                "<< /T (unused) >>",
                "<< /FT /Tx /T (street) /Parent 11 0 R /Type /Annot /Subtype /Widget /Rect [50 440 300 460] /P 3 0 R >>",
                "<< /Length 0 >>\nstream\n\nendstream"
            };
            // The street box is object 13; object 12 is a plain group placeholder kept so numbers stay simple.
            objects[10] = "<< /T (address) /Kids [13 0 R] >>";
            var sb = new StringBuilder("%PDF-1.7\n");
            var offsets = new List<int>();
            for (var i = 0; i < objects.Count; i++)
            {
                offsets.Add(Encoding.ASCII.GetByteCount(sb.ToString()));
                sb.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
            }
            var xref = Encoding.ASCII.GetByteCount(sb.ToString());
            sb.Append("xref\n0 ").Append(objects.Count + 1).Append("\n0000000000 65535 f \n");
            foreach (var o in offsets) sb.Append(o.ToString("D10")).Append(" 00000 n \n");
            sb.Append("trailer\n<< /Size ").Append(objects.Count + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF\n");
            File.WriteAllText(path, sb.ToString(), Encoding.ASCII);
            return path;
        }

        public static void Forms(string folder, Action<bool, string> check)
        {
            UseTestFont();
            var form = MakeForm(Path.Combine(folder, "application.pdf"));
            var before = File.ReadAllBytes(form);
            var fields = PdfForms.Read(form);
            string Kinds() => string.Join(" ", fields.Select(f => f.Name + ":" + f.Kind));
            check(Kinds() == "name:Text notes:Text agree:Tick size:Option city:Choice address.street:Text", "every field, in order: " + Kinds());
            var name = fields.First(f => f.Name == "name");
            check(name.Label == "Your full name" && name.Value == "Old name", "a field's tooltip is its label, and its value is read");
            check(fields.First(f => f.Name == "notes").MultiLine, "a box for several lines");
            check(string.Join(",", fields.First(f => f.Name == "size").Choices) == "Small,Large", "round options' choices");
            check(string.Join(",", fields.First(f => f.Name == "city").Choices) == "Kochi,Delhi,Mumbai", "a list's choices (shown texts): " + string.Join(",", fields.First(f => f.Name == "city").Choices));

            var filled = PdfForms.Fill(form, null, new Dictionary<string, string>
            {
                ["name"] = "Azeem Bukhari", ["notes"] = "Line one\nLine two", ["agree"] = "Yes", ["size"] = "Large", ["city"] = "Mumbai", ["address.street"] = "Marine Drive 7 (മലയാളം)"
            });
            check(filled.EndsWith("application_filled.pdf"), "saved as a new file: " + Path.GetFileName(filled));
            check(File.ReadAllBytes(form).SequenceEqual(before), "the original form isn't changed");
            var again = PdfForms.Read(filled).ToDictionary(f => f.Name, f => f.Value);
            check(again["name"] == "Azeem Bukhari" && again["notes"] == "Line one\nLine two" && again["agree"] == "Yes" && again["size"] == "Large" && again["city"] == "Mumbai",
                  "the values are in the new file: " + string.Join("; ", again.Select(p => p.Key + "=" + p.Value)));
            check(again["address.street"] == "Marine Drive 7 (മലയാളം)", "text in any language");
            using (var doc = PdfReader.Open(filled, PdfDocumentOpenMode.Import))
                check(PdfForms.HasForm(doc), "still a form");
            using (var doc = PdfReader.Open(filled, PdfDocumentOpenMode.Modify))
            {
                check(doc.AcroForm.Elements.GetBoolean("/NeedAppearances"), "viewers are asked to draw the values");
                var size = doc.AcroForm.Fields["size"];
                var kids = size.Elements.GetArray("/Kids").Elements.Select(k => ((PdfSharp.Pdf.Advanced.PdfReference)k).Value as PdfDictionary).ToList();
                check(kids[0].Elements.GetName("/AS") == "/Off" && kids[1].Elements.GetName("/AS") == "/Large", "the chosen round option is the one shown");
            }

            var notForm = Path.Combine(folder, "plain.pdf");
            using (var doc = new PdfDocument()) { doc.AddPage(); doc.Save(notForm); }
            check(PdfForms.Read(notForm).Count == 0, "a PDF that isn't a form has nothing to fill in");
        }

        public static void Signature(string folder, Action<bool, string> check)
        {
            var pdf = Path.Combine(folder, "contract.pdf");
            using (var doc = new PdfDocument()) { doc.AddPage(); doc.AddPage(); doc.Save(pdf); }
            byte[] png;
            using (var img = new MagickImage(MagickColors.Transparent, 300, 100))
            {
                img.Draw(new ImageMagick.Drawing.Drawables().StrokeColor(MagickColors.Navy).StrokeWidth(4).Line(10, 80, 290, 20));
                png = img.ToByteArray(MagickFormat.Png);
            }
            var signed = PdfStamp.AddPicture(pdf, null, 1, png, 0.6, 0.85, 0.3, 0.1);
            check(signed.EndsWith("contract_signed.pdf"), "saved as a new file");
            using (var doc = PdfReader.Open(signed, PdfDocumentOpenMode.Import))
            {
                check(doc.PageCount == 2, "the same pages");
                string Xobjects(PdfPage p) => p.Resources?.Elements.GetDictionary("/XObject")?.Elements.Count.ToString() ?? "0";
                check(Xobjects(doc.Pages[0]) == "0" && Xobjects(doc.Pages[1]) == "1", "the signature is on the second page only");
            }
            try { PdfStamp.AddPicture(pdf, null, 5, png, 0, 0, 0.1, 0.1); check(false, "a page that isn't there"); }
            catch (ArgumentOutOfRangeException) { }
        }

        public static void Searchable(string folder, Action<bool, string> check)
        {
            UseTestFont();
            var words = new List<WordBox>
            {
                new WordBox { Text = "Invoice", Left = 0.1, Top = 0.1, Width = 0.2, Height = 0.03 },
                new WordBox { Text = "number", Left = 0.32, Top = 0.1, Width = 0.18, Height = 0.03 },
                new WordBox { Text = "4711", Left = 0.52, Top = 0.1, Width = 0.1, Height = 0.03 }
            };
            var scan = Path.Combine(folder, "scan.pdf");
            using (var doc = new PdfDocument()) { doc.AddPage(); doc.Save(scan); }
            var searchable = TextLayer.AddToPdf(scan, null, new List<List<WordBox>> { words });
            check(searchable.EndsWith("scan_searchable.pdf"), "saved as a new file");
            var text = PdfText(searchable);
            if (text != null) check(text.Contains("Invoice") && text.Contains("number") && text.Contains("4711"), "the words can be found and copied: " + text);

            byte[] png;
            using (var img = new MagickImage(MagickColors.White, 600, 800)) png = img.ToByteArray(MagickFormat.Png32);
            var fromPicture = TextLayer.FromPictures(new List<(byte[], int, int, List<WordBox>)> { (png, 600, 800, words) }, Path.Combine(folder, "photo_searchable.pdf"));
            using (var doc = PdfReader.Open(fromPicture, PdfDocumentOpenMode.Import))
                check(doc.PageCount == 1 && Math.Abs(doc.Pages[0].Width.Point - 288) < 0.5, "a page as big as the picture at 150 dots per inch");
            var text2 = PdfText(fromPicture);
            if (text2 != null) check(text2.Contains("Invoice") && text2.Contains("4711"), "a photographed page's words too: " + text2);
        }

        /// <summary>The tests run without Windows' fonts: any font will do for form boxes and invisible text.</summary>
        private static void UseTestFont()
        {
            if (GlobalFontSettings.FontResolver == null && !OperatingSystem.IsWindows()) GlobalFontSettings.FontResolver = new OneFont();
        }

        /// <summary>The text a PDF reader gets from a file, when pdftotext is there (else null).</summary>
        private static string PdfText(string pdf)
        {
            try
            {
                var p = Process.Start(new ProcessStartInfo("pdftotext", "\"" + pdf + "\" -") { RedirectStandardOutput = true, UseShellExecute = false });
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return p.ExitCode == 0 ? output : null;
            }
            catch (Exception) { return null; }
        }

        private sealed class OneFont : IFontResolver
        {
            private static readonly string File = new[] { "/usr/share/fonts/truetype/crosextra/Carlito-Regular.ttf", @"C:\Windows\Fonts\arial.ttf" }.FirstOrDefault(System.IO.File.Exists);
            public FontResolverInfo ResolveTypeface(string familyName, bool bold, bool italic) => new FontResolverInfo("one");
            public byte[] GetFont(string faceName) => System.IO.File.ReadAllBytes(File);
        }
    }
}
