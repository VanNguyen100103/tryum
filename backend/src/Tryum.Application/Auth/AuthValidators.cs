using FluentValidation;

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
            .MaximumLength(20).WithMessage("Số điện thoại tối đa 20 ký tự.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
    }
}

/// <summary>Ràng buộc input khi đăng nhập.</summary>
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email là bắt buộc.")
            .EmailAddress().WithMessage("Email không hợp lệ.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Mật khẩu là bắt buộc.");
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