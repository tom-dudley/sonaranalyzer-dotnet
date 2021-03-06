/*
 * SonarAnalyzer for .NET
 * Copyright (C) 2015-2017 SonarSource SA
 * mailto: contact AT sonarsource DOT com
 *
 * This program is free software; you can redistribute it and/or
 * modify it under the terms of the GNU Lesser General Public
 * License as published by the Free Software Foundation; either
 * version 3 of the License, or (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
 * Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with this program; if not, write to the Free Software Foundation,
 * Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
 */

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SonarAnalyzer.Common;
using SonarAnalyzer.Helpers;
using System.Linq;
using System;

namespace SonarAnalyzer.Rules.CSharp
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    [Rule(DiagnosticId)]
    public class DoNotUseOutRefParameters : SonarDiagnosticAnalyzer
    {
        internal const string DiagnosticId = "S3874";
        private const string MessageFormat = "Consider refactoring this method in order to remove the need for this '{0}' modifier.";

        private static readonly DiagnosticDescriptor rule =
            DiagnosticDescriptorBuilder.GetDescriptor(DiagnosticId, MessageFormat, RspecStrings.ResourceManager);

        protected sealed override DiagnosticDescriptor Rule => rule;

        protected override void Initialize(SonarAnalysisContext context)
        {
            context.RegisterSyntaxNodeActionInNonGenerated(
                c =>
                {
                    var parameter = (ParameterSyntax)c.Node;

                    if (!parameter.Modifiers.Any(IsRefOrOut))
                    {
                        return;
                    }

                    var modifier = parameter.Modifiers.First(IsRefOrOut);

                    var parameterSymbol = c.SemanticModel.GetDeclaredSymbol(parameter);
                    var containingMethod = parameterSymbol?.ContainingSymbol as IMethodSymbol;

                    if (containingMethod == null ||
                        containingMethod.IsOverride ||
                        !containingMethod.IsPublicApi() ||
                        IsTryPattern(containingMethod, modifier))
                    {
                        return;
                    }

                    c.ReportDiagnostic(Diagnostic.Create(Rule, modifier.GetLocation(), modifier.ValueText));
                },
                SyntaxKind.Parameter);
        }

        private bool IsTryPattern(IMethodSymbol method, SyntaxToken modifier)
        {
            return method.Name.StartsWith("Try", StringComparison.Ordinal) &&
                method.ReturnType.Is(KnownType.System_Boolean) && 
                modifier.IsKind(SyntaxKind.OutKeyword);
        }

        private static bool IsRefOrOut(SyntaxToken token)
        {
            return token.IsKind(SyntaxKind.RefKeyword) || token.IsKind(SyntaxKind.OutKeyword);
        }
    }
}
