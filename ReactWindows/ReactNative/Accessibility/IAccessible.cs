namespace ReactNative.Accessibility
{
    /// <summary>
    /// Partial accessibility support: accessibilityTraits.
    /// </summary>
    public interface IAccessible
    {
        /// <summary>
        /// accessibilityTraits property.
        /// </summary>
        AccessibilityTrait[] AccessibilityTraits { get; set; }
    }
}
