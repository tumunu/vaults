using System;

namespace VaultsFunctions.Core.Attributes
{
    /// <summary>
    /// Attribute to specify the required OAuth scope for a function endpoint.
    /// This is used for documentation and validation purposes.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class RequiredScopeAttribute : Attribute
    {
        /// <summary>
        /// The required OAuth scope for accessing this endpoint
        /// </summary>
        public string Scope { get; }

        /// <summary>
        /// Optional description of why this scope is required
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Whether this scope is mandatory (true) or optional (false)
        /// </summary>
        public bool IsMandatory { get; set; } = true;

        /// <summary>
        /// Initializes a new instance of the RequiredScopeAttribute
        /// </summary>
        /// <param name="scope">The required OAuth scope</param>
        public RequiredScopeAttribute(string scope)
        {
            if (string.IsNullOrWhiteSpace(scope))
                throw new ArgumentException("Scope cannot be null or empty", nameof(scope));
            
            Scope = scope;
        }
    }
}