namespace CleanArchitecture.Application.Common.Interfaces;

public interface IUser
{
    Guid? Id { get; }
    List<string>? Roles { get; }

}
