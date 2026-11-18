using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Kuduck.Services;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.AcroForms;
using PdfSharp.Pdf.Advanced;
using PdfSharp.Pdf.IO;

namespace Kuduck.Tools
{
    public enum FormFieldKind { Text, Tick, Choice, Option }

    /// <summary>One thing to fill in on a PDF form: a box to type in, a tick box, a list to choose from, or round options.</summary>
    public sealed class FormField
    {
        /// <summary>The field's full name in the PDF ("address.street"), which identifies it.</summary>
        public string Name { get; set; }
        /// <summary>What it's called for people: its tooltip if it has one, else its name.</summary>
        public string Label { get; set; }
        public FormFieldKind Kind { get; set; }
        /// <summary>The text, the chosen item, or "Yes"/"Off" for a tick box.</summary>
        public string Value { get; set; }
        public List<string> Choices { get; set; } = new List<string>();
        public bool MultiLine { get; set; }
        public bool ReadOnly { get; set; }
    }

    /// <summary>
    /// Fills in PDF forms. The form is opened as it is (so it stays a form), the values are written into its fields, and
    /// PDF viewers are asked to draw the filled-in text themselves, as Acrobat, Edge and Chrome do. The result is a new
    /// file; the original isn't changed.
    /// </summary>
    public static class PdfForms
    {
        public static PdfDocument OpenForChanges(string path, string password)
        {
            PdfFonts.Ensure(); // reading a form's boxes needs their fonts
            try
            {
                return string.IsNullOrEmpty(password)
                    ? PdfReader.Open(path, PdfDocumentOpenMode.Modify)
                    : PdfReader.Open(path, password, PdfDocumentOpenMode.Modify);
            }
            catch (Exception ex) when (ex.Message.IndexOf("password", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                throw new InvalidOperationException(string.IsNullOrEmpty(password)
                    ? Lang.T("\"{0}\" has a password. Type it in the password box.", Path.GetFileName(path))
                    : Lang.T("That password isn't right."));
            }
        }

        /// <summary>The fields of a PDF form, in the order they're listed (empty when the PDF isn't a form).</summary>
        public static List<FormField> Read(string path, string password = null)
        {
            using (PdfDocument doc = OpenForChanges(path, password))
            {
                List<FormField> list = new List<FormField>();
                if (!HasForm(doc)) return list;
                foreach (PdfAcroField field in Leaves(doc.AcroForm.Fields, string.Empty))
                {
                    FormField f = Describe(field);
                    if (f != null) list.Add(f);
                }
                return list;
            }
        }

        /// <summary>Writes the values (by field name) into a copy of the form. Returns the new file's path.</summary>
        public static string Fill(string path, string password, IDictionary<string, string> values, string output = null)
        {
            output = output ?? FileHelpers.OutputPath(path, "_filled", ".pdf");
            using (PdfDocument doc = OpenForChanges(path, password))
            {
                if (!HasForm(doc)) throw new InvalidOperationException(Lang.T("This PDF has no form to fill in."));
                foreach (PdfAcroField field in Leaves(doc.AcroForm.Fields, string.Empty))
                {
                    string name = FullName(field);
                    if (!values.TryGetValue(name, out string value) || field.ReadOnly) continue;
                    Set(field, value ?? string.Empty);
                }
                // Viewers draw the filled-in values themselves.
                doc.AcroForm.Elements.SetBoolean("/NeedAppearances", true);
                doc.Info.Creator = "KUDUCK";
                doc.Save(output);
            }
            return output;
        }

        /// <summary>Whether a PDF has a form (asking for one that isn't there throws).</summary>
        public static bool HasForm(PdfDocument doc) => doc.Internals.Catalog.Elements.ContainsKey("/AcroForm");

        private static IEnumerable<PdfAcroField> Leaves(PdfAcroField.PdfAcroFieldCollection fields, string prefix)
        {
            for (int i = 0; i < fields.Count; i++)
            {
                PdfAcroField field = fields[i];
                if (field == null) continue;
                // Round options keep their choices as "kids" that are only places on the page, not fields of their own.
                bool kidsAreFields = field.HasKids && !(field is PdfRadioButtonField) && !(field is PdfCheckBoxField) && !(field is PdfTextField) && !(field is PdfChoiceField);
                if (kidsAreFields) foreach (PdfAcroField leaf in Leaves(field.Fields, FullName(field) + ".")) yield return leaf;
                else yield return field;
            }
        }

        /// <summary>A field's full name: its own name after its parents' ("address.street").</summary>
        public static string FullName(PdfAcroField field)
        {
            List<string> parts = new List<string>();
            PdfDictionary d = field;
            for (int guard = 0; d != null && guard < 32; guard++)
            {
                string t = d.Elements.GetString("/T");
                if (!string.IsNullOrEmpty(t)) parts.Insert(0, t);
                d = d.Elements.GetObject("/Parent") as PdfDictionary ?? (d.Elements["/Parent"] as PdfReference)?.Value as PdfDictionary;
            }
            return string.Join(".", parts);
        }

        private static FormField Describe(PdfAcroField field)
        {
            string name = FullName(field);
            if (string.IsNullOrEmpty(name)) return null;
            string tooltip = field.Elements.GetString("/TU");
            FormField f = new FormField { Name = name, Label = string.IsNullOrWhiteSpace(tooltip) ? name : tooltip, ReadOnly = field.ReadOnly };
            switch (field)
            {
                case PdfTextField text:
                    f.Kind = FormFieldKind.Text;
                    f.Value = ValueText(field);
                    f.MultiLine = text.MultiLine;
                    if (text.Password) return null; // a password box isn't something to save in a file
                    break;
                case PdfCheckBoxField tick:
                    f.Kind = FormFieldKind.Tick;
                    f.Value = tick.Checked ? "Yes" : "Off";
                    break;
                case PdfRadioButtonField radio:
                    f.Kind = FormFieldKind.Option;
                    f.Choices = OnNames(radio);
                    f.Value = ValueText(field);
                    if (f.Choices.Count == 0) return null;
                    break;
                case PdfChoiceField choice:
                    f.Kind = FormFieldKind.Choice;
                    f.Choices = Options(choice);
                    f.Value = ValueText(field);
                    break;
                default:
                    return null; // push buttons and signature fields: nothing to fill in
            }
            return f;
        }

        private static string ValueText(PdfAcroField field)
        {
            PdfItem v = field.Elements["/V"];
            if (v is PdfReference r) v = r.Value;
            switch (v)
            {
                case PdfString s: return s.Value;
                case PdfName n: return n.Value.TrimStart('/');
                default: return string.Empty;
            }
        }

        /// <summary>The items of a list: plain texts, or [shown text, value] pairs (the shown text is used).</summary>
        private static List<string> Options(PdfChoiceField field)
        {
            List<string> list = new List<string>();
            PdfArray opt = field.Elements.GetArray("/Opt");
            if (opt == null) return list;
            foreach (PdfItem item in opt.Elements)
            {
                PdfItem i = item is PdfReference r ? r.Value : item;
                if (i is PdfString s) list.Add(s.Value);
                else if (i is PdfArray pair && pair.Elements.Count >= 2) list.Add(pair.Elements.GetString(1));
            }
            return list;
        }

        /// <summary>The names of a set of round options (each place on the page has its own "on" name).</summary>
        private static List<string> OnNames(PdfRadioButtonField field)
        {
            List<string> names = new List<string>();
            PdfArray kids = field.Elements.GetArray("/Kids");
            if (kids == null) return names;
            foreach (PdfItem kid in kids.Elements)
            {
                PdfDictionary k = (kid as PdfReference)?.Value as PdfDictionary ?? kid as PdfDictionary;
                PdfDictionary normal = k?.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
                string on = normal?.Elements.Keys.Select(x => x.TrimStart('/')).FirstOrDefault(x => x != "Off");
                if (on != null && !names.Contains(on)) names.Add(on);
            }
            return names;
        }

        private static void Set(PdfAcroField field, string value)
        {
            switch (field)
            {
                case PdfTextField _:
                    field.Elements.SetValue("/V", new PdfString(value, PdfStringEncoding.Unicode));
                    // The old drawing of the box would show the old text: the viewer draws it again.
                    foreach (PdfDictionary widget in Widgets(field)) widget.Elements.Remove("/AP");
                    break;
                case PdfCheckBoxField tick:
                    tick.Checked = value == "Yes" || value == "On" || value == "true";
                    break;
                case PdfRadioButtonField radio:
                    List<string> names = OnNames(radio);
                    int index = names.IndexOf(value);
                    radio.Elements.SetName("/V", index >= 0 ? "/" + value : "/Off");
                    PdfArray kids = radio.Elements.GetArray("/Kids");
                    if (kids != null)
                        foreach (PdfItem kid in kids.Elements)
                        {
                            PdfDictionary k = (kid as PdfReference)?.Value as PdfDictionary ?? kid as PdfDictionary;
                            if (k == null) continue;
                            PdfDictionary normal = k.Elements.GetDictionary("/AP")?.Elements.GetDictionary("/N");
                            bool mine = normal != null && normal.Elements.ContainsKey("/" + value);
                            k.Elements.SetName("/AS", mine ? "/" + value : "/Off");
                        }
                    break;
                case PdfChoiceField _:
                    field.Elements.SetValue("/V", new PdfString(value, PdfStringEncoding.Unicode));
                    foreach (PdfDictionary widget in Widgets(field)) widget.Elements.Remove("/AP");
                    break;
            }
        }

        /// <summary>The places on the page where a field is drawn: the field itself, or its kids.</summary>
        private static IEnumerable<PdfDictionary> Widgets(PdfAcroField field)
        {
            PdfArray kids = field.Elements.GetArray("/Kids");
            if (kids == null) { yield return field; yield break; }
            foreach (PdfItem kid in kids.Elements)
                if (((kid as PdfReference)?.Value ?? kid) is PdfDictionary d) yield return d;
        }
    }

    /// <summary>Puts a picture (a signature) on a page of a PDF, as a new file.</summary>
    public static class PdfStamp
    {
        /// <summary>
        /// Draws <paramref name="png"/> on page <paramref name="pageIndex"/> (from 0). The place is given as parts of the
        /// page as it's shown (0 to 1 from the top-left corner), so it's the same whatever the page's size or turn.
        /// </summary>
        public static string AddPicture(string path, string password, int pageIndex, byte[] png, double left, double top, double width, double height, string output = null)
        {
            output = output ?? FileHelpers.OutputPath(path, "_signed", ".pdf");
            using (PdfDocument doc = PdfForms.OpenForChanges(path, password))
            {
                if (pageIndex < 0 || pageIndex >= doc.PageCount) throw new ArgumentOutOfRangeException(nameof(pageIndex), Lang.T("There's no page {0} in this PDF.", pageIndex + 1));
                PdfPage page = doc.Pages[pageIndex];
                using (XGraphics gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append))
                using (MemoryStream ms = new MemoryStream(png))
                using (XImage image = XImage.FromStream(ms))
                {
                    double w = gfx.PageSize.Width, h = gfx.PageSize.Height;
                    gfx.DrawImage(image, left * w, top * h, width * w, height * h);
                }
                doc.Info.Creator = "KUDUCK";
                doc.Save(output);
            }
            return output;
        }
    }

    /// <summary>A word found on a scanned page, with where it is (parts of the page, 0 to 1 from the top-left corner).</summary>
    public sealed class WordBox
    {
        public string Text { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }

    /// <summary>
    /// Makes a PDF you can search and copy text from: the words read from each scanned page are written over it as
    /// invisible text, in the same places, so selecting and copying in any PDF viewer gives the words.
    /// </summary>
    public static class TextLayer
    {
        /// <summary>The original PDF with the words added over its pages (as a new file).</summary>
        public static string AddToPdf(string path, string password, IList<List<WordBox>> pages, string output = null)
        {
            output = output ?? FileHelpers.OutputPath(path, "_searchable", ".pdf");
            PdfFonts.Ensure();
            using (PdfDocument doc = PdfForms.OpenForChanges(path, password))
            {
                for (int i = 0; i < Math.Min(doc.PageCount, pages.Count); i++)
                    using (XGraphics gfx = XGraphics.FromPdfPage(doc.Pages[i], XGraphicsPdfPageOptions.Append))
                        Write(gfx, pages[i]);
                doc.Info.Creator = "KUDUCK";
                doc.Save(output);
            }
            return output;
        }

        /// <summary>A new PDF of photographed or scanned pictures, each a page, with their words over them. The pictures are
        /// PNG files with full colour (32 bits), which is what the PDF library reads.</summary>
        public static string FromPictures(IList<(byte[] Png, int Width, int Height, List<WordBox> Words)> pictures, string output)
        {
            PdfFonts.Ensure();
            using (PdfDocument doc = new PdfDocument())
            {
                foreach (var p in pictures)
                {
                    PdfPage page = doc.AddPage();
                    // As big as the picture at 150 dots per inch, which prints like a scan.
                    double w = p.Width * 72.0 / 150, h = p.Height * 72.0 / 150;
                    page.MediaBox = new PdfRectangle(new XPoint(0, 0), new XPoint(w, h));
                    using (XGraphics gfx = XGraphics.FromPdfPage(page))
                    using (MemoryStream ms = new MemoryStream(p.Png))
                    using (XImage image = XImage.FromStream(ms))
                    {
                        gfx.DrawImage(image, 0, 0, w, h);
                        Write(gfx, p.Words);
                    }
                }
                doc.Info.Creator = "KUDUCK";
                doc.Options.CompressContentStreams = true;
                doc.Save(output);
            }
            return output;
        }

        private static readonly XBrush Invisible = new XSolidBrush(XColor.FromArgb(0, 0, 0, 0));

        private static void Write(XGraphics gfx, IEnumerable<WordBox> words)
        {
            double pw = gfx.PageSize.Width, ph = gfx.PageSize.Height;
            foreach (WordBox word in words)
            {
                if (string.IsNullOrWhiteSpace(word.Text) || word.Width <= 0 || word.Height <= 0) continue;
                double x = word.Left * pw, y = word.Top * ph, w = word.Width * pw, h = word.Height * ph;
                XFont font = PdfFonts.For(word.Text, Math.Max(1, h * 0.9));
                double measured = gfx.MeasureString(word.Text, font).Width;
                if (measured <= 0) continue;
                // Stretched to the word's width, so a selection covers the word on the scan.
                XGraphicsState state = gfx.Save();
                gfx.TranslateTransform(x, y);
                gfx.ScaleTransform(w / measured, 1);
                gfx.DrawString(word.Text, font, Invisible, new XPoint(0, 0), XStringFormats.TopLeft);
                gfx.Restore(state);
            }
        }
    }

    /// <summary>The fonts the text layer is written in: Windows' own (or, for the tests, whatever was set).</summary>
    public static class PdfFonts
    {
        private static bool _ready;

        public static void Ensure()
        {
            if (_ready) return;
            _ready = true;
            if (GlobalFontSettings.FontResolver != null) return;
            try { if (OperatingSystem.IsWindows()) GlobalFontSettings.UseWindowsFontsUnderWindows = true; }
            catch (InvalidOperationException) { /* already set */ }
        }

        /// <summary>A font with the word's letters: Nirmala UI for Malayalam and Hindi, Arial for the rest.</summary>
        public static XFont For(string text, double size)
        {
            bool indic = text.Any(c => c >= 'ऀ' && c <= '෿');
            XPdfFontOptions unicode = new XPdfFontOptions(PdfFontEncoding.Unicode);
            if (indic)
            {
                try { return new XFont("Nirmala UI", size, XFontStyleEx.Regular, unicode); }
                catch (Exception) { /* not there: the usual font */ }
            }
            return new XFont("Arial", size, XFontStyleEx.Regular, unicode);
        }
    }
}
