﻿/**** Git Credential Manager for Windows ****
 *
 * Copyright (c) GitHub Corporation
 * All rights reserved.
 *
 * MIT License
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the """"Software""""), to deal
 * in the Software without restriction, including without limitation the rights to
 * use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of
 * the Software, and to permit persons to whom the Software is furnished to do so,
 * subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED *AS IS*, WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS
 * FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR
 * COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN
 * AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
 * WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE."
**/

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;

namespace GitHub.Authentication.ViewModels.Validation
{
    public abstract class PropertyValidator: ViewModel
    {
        /// <summary>
        /// Creates a validator for a property. This validator is the starting point to attach other
        /// validations to the property. This method itself doesn't apply any validations.
        /// </summary>
        /// <typeparam name="TObject">Type of the object with the property to validate.</typeparam>
        /// <typeparam name="TProperty">The type of the property to validate.</typeparam>
        /// <param name="source">The object with the property to validate.</param>
        /// <param name="property">An expression for the property to validate</param>
        /// <returns>A property validator</returns>
        public static PropertyValidator<TObject, TProperty> For<TObject, TProperty>(TObject source, Expression<Func<TObject, TProperty>> property)
            where TObject : INotifyPropertyChanged
        {
            return new PropertyValidator<TObject, TProperty>(source, property);
        }

        private PropertyValidationResult _validationResult = PropertyValidationResult.Unvalidated;

        /// <summary>
        /// The current validation result for this validator.
        /// </summary>
        public PropertyValidationResult ValidationResult
        {
            get
            {
                return _validationResult;
            }
            protected set
            {
                _validationResult = value;
                RaisePropertyChangedEvent(nameof(ValidationResult));
            }
        }
    }

    public class PropertyValidator<TProperty>: PropertyValidator
    {
        // This should only be used by PropertyValidator<TObject, TProperty>
        protected PropertyValidator() { }

        protected PropertyValidator(PropertyValidator<TProperty> previousValidator)
            : this(previousValidator, _ => PropertyValidationResult.Unvalidated) { }

        internal PropertyValidator(PropertyValidator<TProperty> previousValidator, Func<TProperty, PropertyValidationResult> validation)
        {
            if (previousValidator == null) throw new ArgumentNullException(nameof(previousValidator));
            if (validation == null) throw new ArgumentNullException(nameof(validation));

            previousValidator.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(CurrentPropertyValue)) return;

                if (previousValidator.ValidationResult.Status == ValidationStatus.Invalid)
                {
                    // If any validator is invalid, we don't need to run the rest of the chained validators.
                    ValidationResult = previousValidator.ValidationResult;
                }
                else
                {
                    ValidationResult = validation(previousValidator.CurrentPropertyValue);
                    NotifyNextValidator(previousValidator.CurrentPropertyValue);
                }
            };
        }

        private TProperty _currentValue;

        protected TProperty CurrentPropertyValue
        {
            get { return _currentValue; }
            set
            {
                _currentValue = value;
                RaisePropertyChangedEvent(nameof(CurrentPropertyValue));
            }
        }

        protected virtual void NotifyNextValidator(TProperty currentValue)
        {
            CurrentPropertyValue = currentValue;
        }
    }

    /// <summary>
    /// This validator watches the target property for changes and then propagates that change up the chain.
    /// </summary>
    /// <typeparam name="TObject"></typeparam>
    /// <typeparam name="TProperty"></typeparam>
    public class PropertyValidator<TObject, TProperty>: PropertyValidator<TProperty> where TObject : INotifyPropertyChanged
    {
        internal PropertyValidator(TObject source, Expression<Func<TObject, TProperty>> propertyExpression)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (propertyExpression == null) throw new ArgumentNullException(nameof(propertyExpression));

            var compiledProperty = propertyExpression.Compile();
            var propertyInfo = GetPropertyInfo(propertyExpression);
            // Start watching for changes to this property and propagate those changes to the chained validators.
            source.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == propertyInfo.Name)
                {
                    // This will propagate this chain up the validator stack.
                    NotifyNextValidator(compiledProperty(source));
                }
            };
        }

        private static PropertyInfo GetPropertyInfo(Expression<Func<TObject, TProperty>> propertyExpression)
        {
            var member = propertyExpression.Body as MemberExpression;
            Debug.Assert(member != null, "Property expression doesn't refer to a member.");

            var propertyInfo = member.Member as PropertyInfo;
            Debug.Assert(propertyInfo != null, "Property expression does not refer to a property.");

            var propertyType = typeof(TObject);

            Debug.Assert(propertyType == propertyInfo.ReflectedType
                || propertyType.IsSubclassOf(propertyInfo.ReflectedType), "Property expression is not of the specified type");

            return propertyInfo;
        }
    }
}
