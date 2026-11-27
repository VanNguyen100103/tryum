using Xunit;
using Tryum.Application.Common;

namespace Tryum.UnitTests.AiGenerated
{
    public class PhoneNormalizerAiTests
    {
        [Fact]
        public void TryNormalize_ReturnsTrueAndCorrectE164_ForStandardLocalNumber()
        {
            var input = "0901234567";
            var result = PhoneNormalizer.TryNormalize(input, out var e164);

            Assert.True(result);
            Assert.Equal("+84901234567", e164);
        }

        [Fact]
        public void TryNormalize_ReturnsTrueAndCorrectE164_ForInternationalWithSpaces()
        {
            var input = "+84 901 234 567";
            var result = PhoneNormalizer.TryNormalize(input, out var e164);

            Assert.True(result);
            Assert.Equal("+84901234567", e164);
        }

        [Fact]
        public void TryNormalize_ReturnsTrueAndCorrectE164_ForInternationalWithoutPlus()
        {
            var input = "84901234567";
            var result = PhoneNormalizer.TryNormalize(input, out var e164);

            Assert.True(result);
            Assert.Equal("+84901234567", e164);
        }

        [Fact]
        public void TryNormalize_ReturnsTrueAndCorrectE164_ForInternationalWith0084Prefix()
        {
            var input = "0084 901 234 567";
            var result = PhoneNormalizer.TryNormalize(input, out var e164);

            Assert.True(result);
            Assert.Equal("+84901234567", e164);
        }

        [Fact]
        public void TryNormalize_ReturnsFalse_ForNullInput()
        {
            var result = PhoneNormalizer.TryNormalize(null, out var e164);

            Assert.False(result);
            Assert.Equal(string.Empty, e164);
        }

        [Fact]
        public void TryNormalize_ReturnsFalse_ForEmptyString()
        {
            var result = PhoneNormalizer.TryNormalize(string.Empty, out var e164);

            Assert.False(result);
            Assert.Equal(string.Empty, e164);
        }

        [Fact]
        public void TryNormalize_ReturnsFalse_ForWhitespaceOnly()
        {
            var result = PhoneNormalizer.TryNormalize("   ", out var e164);

            Assert.False(result);
            Assert.Equal(string.Empty, e164);
        }

        [Fact]
        public void TryNormalize_ReturnsFalse_ForNonNumericInput()
        {
            var result = PhoneNormalizer.TryNormalize("abcdefg", out var e164);

            Assert.False(result);
            Assert.Equal(string.Empty, e164);
        }

        [Fact]
        public void TryNormalize_ReturnsFalse_ForInvalidPrefix()
        {
            var result = PhoneNormalizer.TryNormalize("0123456789", out var e164);

            Assert.False(result);
            Assert.Equal(string.Empty, e164);
        }

        [Fact]
        public void TryNormalize_ReturnsFalse_ForIncorrectLengthAfterCountryCode()
        {
            var result = PhoneNormalizer.TryNormalize("8490123456", out var e164); // only 10 digits total

            Assert.False(result);
            Assert.Equal(string.Empty, e164);
        }

        [Fact]
        public void FormatForDisplay_ReturnsFormattedString_ForValidE164()
        {
            var input = "+84901234567";
            var formatted = PhoneNormalizer.FormatForDisplay(input);

            Assert.Equal("0901.234.567", formatted);
        }

        [Fact]
        public void FormatForDisplay_ReturnsOriginal_WhenBodyLengthIsNotNine()
        {
            var input = "+84123";
            var formatted = PhoneNormalizer.FormatForDisplay(input);

            Assert.Equal(input, formatted);
        }

        [Fact]
        public void FormatForDisplay_HandlesInputWithoutPlusPrefix()
        {
            var input = "84901234567";
            var formatted = PhoneNormalizer.FormatForDisplay(input);

            // Since the method treats the whole string as body (length 11), it returns the original.
            Assert.Equal(input, formatted);
        }

        [Fact]
        public void FormatForDisplay_ReturnsFormattedString_ForDifferentValidPrefix()
        {
            var input = "+84123456789";
            var formatted = PhoneNormalizer.FormatForDisplay(input);

            Assert.Equal("0123.456.789", formatted);
        }
    }
}
