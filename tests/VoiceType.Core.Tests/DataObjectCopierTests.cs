using System.Windows;
using VoiceType.Core.Insertion;

namespace VoiceType.Core.Tests;

public class DataObjectCopierTests
{
    [Fact]
    public void CopiesTextAndFileDropTogether()
    {
        var source = new DataObject();
        source.SetData(DataFormats.UnicodeText, "hello", autoConvert: false);
        source.SetData(DataFormats.FileDrop, new[] { @"C:\temp\a.txt", @"C:\temp\b.txt" }, autoConvert: false);

        var (copy, copied, failed) = DataObjectCopier.Copy(source);

        Assert.Equal(0, failed);
        Assert.Equal(2, copied);
        Assert.Equal("hello", copy.GetData(DataFormats.UnicodeText, autoConvert: false));
        Assert.Equal(new[] { @"C:\temp\a.txt", @"C:\temp\b.txt" }, copy.GetData(DataFormats.FileDrop, autoConvert: false));
    }

    [Fact]
    public void CopiesRichTextAndHtmlFormats()
    {
        var source = new DataObject();
        source.SetData(DataFormats.Rtf, @"{\rtf1 hi}", autoConvert: false);
        source.SetData(DataFormats.Html, "<b>hi</b>", autoConvert: false);
        source.SetData(DataFormats.UnicodeText, "hi", autoConvert: false);

        var (copy, copied, failed) = DataObjectCopier.Copy(source);

        Assert.Equal(0, failed);
        Assert.Equal(3, copied);
        Assert.Equal(@"{\rtf1 hi}", copy.GetData(DataFormats.Rtf, autoConvert: false));
        Assert.Equal("<b>hi</b>", copy.GetData(DataFormats.Html, autoConvert: false));
    }

    [Fact]
    public void FormatThatThrows_IsSkippedNotFatal()
    {
        var source = new ThrowingDataObject("good", throwingFormat: "Poison");

        var (copy, copied, failed) = DataObjectCopier.Copy(source);

        Assert.Equal(1, copied);
        Assert.Equal(1, failed);
        Assert.Equal("good", copy.GetData(DataFormats.UnicodeText, autoConvert: false));
        Assert.False(copy.GetDataPresent("Poison", autoConvert: false));
    }

    private sealed class ThrowingDataObject : IDataObject
    {
        private readonly string _text;
        private readonly string _throwingFormat;

        public ThrowingDataObject(string text, string throwingFormat)
        {
            _text = text;
            _throwingFormat = throwingFormat;
        }

        public string[] GetFormats(bool autoConvert) => new[] { DataFormats.UnicodeText, _throwingFormat };
        public string[] GetFormats() => GetFormats(false);

        public bool GetDataPresent(string format, bool autoConvert) =>
            format == DataFormats.UnicodeText || format == _throwingFormat;
        public bool GetDataPresent(string format) => GetDataPresent(format, false);
        public bool GetDataPresent(Type format) => false;

        public object GetData(string format, bool autoConvert) =>
            format == _throwingFormat
                ? throw new InvalidOperationException("delayed render failed")
                : _text;
        public object GetData(string format) => GetData(format, false);
        public object GetData(Type format) => throw new NotSupportedException();

        public void SetData(string format, object data, bool autoConvert) => throw new NotSupportedException();
        public void SetData(string format, object data) => throw new NotSupportedException();
        public void SetData(Type format, object data) => throw new NotSupportedException();
        public void SetData(object data) => throw new NotSupportedException();
    }
}
