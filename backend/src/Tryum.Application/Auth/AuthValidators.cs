using System.ComponentModel.DataAnnotations;
using FluentValidation;
using Tryum.Application.Common;

namespace Tryum.Application.Auth;

/// <summary>Ràng buộc input khi đăng ký cửa hàng mới.</summary>
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.ShopName)
            .NotEmpty().WithMessage("Tên cửa hàng là bắt buộc.")
            .MaximumLength(200);

        RuleFor(x => x.OwnerName)
            .NotEmpty().WithMessage("Họ tên chủ cửa hàng là bắt buộc.")
            .MaximumLength(200);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email là bắt buộc.")
            .EmailAddress().WithMessage("Email không hợp lệ.")
            .MaximumLength(256);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Mật khẩu là bắt buộc.")
            .MinimumLength(8).WithMessage("Mật khẩu tối thiểu 8 ký tự.")
            .Matches("[A-Za-z]").WithMessage("Mật khẩu phải có ít nhất 1 chữ cái.")
            .Matches("[0-9]").WithMessage("Mật khẩu phải có ít nhất 1 chữ số.");

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Số điện thoại là bắt buộc.")
            .MaximumLength(20).WithMessage("Số điện thoại tối đa 20 ký tự.")
            .Must(phone => PhoneNormalizer.TryNormalize(phone, out _))
            .WithMessage("Số điện thoại di động Việt Nam không hợp lệ.");

        RuleFor(x => x.OtpCode)
            .NotEmpty().WithMessage("Mã OTP là bắt buộc.")
            .Matches(@"^\d{6}$").WithMessage("Mã OTP phải gồm 6 chữ số.");
    }
}

/// <summary>Ràng buộc input khi gửi mã OTP.</summary>
public sealed class SendOtpRequestValidator : AbstractValidator<SendOtpRequest>
{
    public SendOtpRequestValidator()
    {
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Số điện thoại là bắt buộc.")
            .MaximumLength(20).WithMessage("Số điện thoại tối đa 20 ký tự.")
            .Must(phone => PhoneNormalizer.TryNormalize(phone, out _))
            .WithMessage("Số điện thoại di động Việt Nam không hợp lệ.");

        // Captcha do server sinh: bắt buộc có id + mã để chặn bot đốt quota SMS.
        // (Bỏ qua khi IsResend = true – người dùng đã vượt captcha ở lần gửi đầu.)
        RuleFor(x => x.CaptchaId)
            .NotEmpty()
            .When(x => !x.IsResend)
            .WithMessage("Mã xác thực không hợp lệ. Vui lòng tải lại ảnh mã.");

        RuleFor(x => x.CaptchaCode)
            .NotEmpty()
            .When(x => !x.IsResend)
            .WithMessage("Vui lòng nhập mã xác thực.")
            .Length(4)
            .When(x => !x.IsResend)
            .WithMessage("Mã xác thực gồm 4 ký tự.");
    }
}

/// <summary>Ràng buộc input khi đăng nhập (nhận email hoặc SĐT).</summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage("Số điện thoại hoặc email là bắt buộc.")
            .Must(BeValidIdentifier)
            .WithMessage("Nhập số điện thoại di động Việt Nam hoặc email hợp lệ.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Mật khẩu là bắt buộc.");
    }

    /// <summary>Có dấu @ thì validate email, ngược lại validate SĐT di động VN.</summary>
    private static bool BeValidIdentifier(string identifier)
    {
        var value = identifier.Trim();

        if (value.Contains('@', StringComparison.Ordinal))
            return new EmailAddressAttribute().IsValid(value);

        return PhoneNormalizer.TryNormalize(value, out _);
    }
}

/// <summary>Ràng buộc input khi refresh token.</summary>
public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty().WithMessage("Refresh token là bắt buộc.");
    }
}
