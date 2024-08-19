using DigitalPreservation.Core.Guard;
using FluentAssertions;

namespace DigitalPreservation.Core.Tests.Guard;

public class GuardTests
{
    [Fact]
    public void ThrowIfNull_Throws_IfArgumentNull()
    {
        // Act
        Action action = () => GuardX.ThrowIfNull<object>(null, "foo");
        
        // Assert
        action.Should()
            .Throw<ArgumentNullException>()
            .WithMessage("Value cannot be null. (Parameter 'foo')");
    } 
    
    [Fact]
    public void ThrowIfNull_ReturnsProvidedValue_IfNotNull()
    {
        // Arrange
        object val = DateTime.Now;
        
        // Act
        var actual = val.ThrowIfNull(nameof(val));
        
        // Assert
        actual.Should().Be(val);
    }
    
    [Fact]
    public void ThrowIfNull_NullableStruct_Throws_IfArgumentNull()
    {
        // Act
        Action action = () => GuardX.ThrowIfNull<int?>(null, "foo");
        
        // Assert
        action.Should()
            .Throw<ArgumentNullException>()
            .WithMessage("Value cannot be null. (Parameter 'foo')");
    } 
    
    [Fact]
    public void ThrowIfNull_NullableStruct_ReturnsProvidedValue_IfNotNull()
    {
        // Arrange
        int? val = 12345;
        
        // Act
        var actual = val.ThrowIfNull(nameof(val));
        
        // Assert
        actual.Should().Be(val);
    }
}