﻿/*
 * SonarQube C# Code Analysis
 * Copyright (C) 2015 SonarSource
 * dev@sonar.codehaus.org
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
 * You should have received a copy of the GNU Lesser General Public
 * License along with this program; if not, write to the Free Software
 * Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02
 */

using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using SonarQube.CSharp.CodeAnalysis.Helpers;
using SonarQube.CSharp.CodeAnalysis.SonarQube.Settings;
using SonarQube.CSharp.CodeAnalysis.SonarQube.Settings.Sqale;

namespace SonarQube.CSharp.CodeAnalysis.Rules
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    [SqaleSubCharacteristic(SqaleSubCharacteristic.LogicReliability)]
    [SqaleConstantRemediation("10min")]
    [Rule(DiagnosticId, RuleSeverity, Title, IsActivatedByDefault)]
    [Tags("bug", "cwe", "denial-of-service", "security")]
    public class NotDisposedIDisposableField : DiagnosticAnalyzer
    {
        internal const string DiagnosticId = "S2930";
        internal const string Title = "\"IDisposable\" members should be disposed";
        internal const string Description =
            "You can't rely on garbage collection to clean up everything. Specifically, you can't " +
            "count on it to release non-memory resources such as \"File\"s. For that, there's the " +
            "\"IDisposable\" interface, and the contract that \"Dispose\" will always be called on " +
            "such objects. When an \"IDisposable\" is a class member, then it's up to that class " +
            "to call \"Dispose\" on it, ideally in its own \"Dispose\" method.";
        internal const string MessageFormat = "\"Dispose\" of \"{0}\".";
        internal const string Category = "SonarQube";
        internal const Severity RuleSeverity = Severity.Critical; 
        internal const bool IsActivatedByDefault = true;

        internal static readonly DiagnosticDescriptor Rule =
            new DiagnosticDescriptor(DiagnosticId, Title, MessageFormat, Category,
                RuleSeverity.ToDiagnosticSeverity(), IsActivatedByDefault,
                helpLinkUri: DiagnosticId.GetHelpLink(),
                description: Description);
        
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(Rule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterCompilationStartAction(analysisContext =>
            {
                var disposableFields = ImmutableHashSet<IFieldSymbol>.Empty;
                var fieldsAssigned = ImmutableHashSet<IFieldSymbol>.Empty;
                var fieldsDisposed = ImmutableHashSet<IFieldSymbol>.Empty;

                analysisContext.RegisterSyntaxNodeAction(c =>
                {
                    var field = (FieldDeclarationSyntax)c.Node;

                    foreach (var variableDeclaratorSyntax in field.Declaration.Variables)
                    {
                        var fieldSymbol = c.SemanticModel.GetDeclaredSymbol(variableDeclaratorSyntax) as IFieldSymbol;

                        if (!ClassWithIDisposableMembers.FieldIsRelevant(fieldSymbol))
                        {
                            continue;
                        }

                        disposableFields = disposableFields.Add(fieldSymbol);

                        if (variableDeclaratorSyntax.Initializer == null ||
                            !(variableDeclaratorSyntax.Initializer.Value is ObjectCreationExpressionSyntax))
                        {
                            return;
                        }

                        fieldsAssigned = fieldsAssigned.Add(fieldSymbol);
                    }
                }, SyntaxKind.FieldDeclaration);
                
                analysisContext.RegisterSyntaxNodeAction(c =>
                {
                    var assignment = (AssignmentExpressionSyntax) c.Node;

                    var objectCreation = assignment.Right as ObjectCreationExpressionSyntax;
                    if (objectCreation == null)
                    {
                        return;
                    }

                    var fieldSymbol = c.SemanticModel.GetSymbolInfo(assignment.Left).Symbol as IFieldSymbol;
                    if (!ClassWithIDisposableMembers.FieldIsRelevant(fieldSymbol))
                    {
                        return;
                    }

                    fieldsAssigned = fieldsAssigned.Add(fieldSymbol);

                }, SyntaxKind.SimpleAssignmentExpression);
                
                analysisContext.RegisterSyntaxNodeAction(c =>
                {
                    var invocation = (InvocationExpressionSyntax) c.Node;
                    var memberAccess = invocation.Expression as MemberAccessExpressionSyntax;
                    if (memberAccess == null)
                    {
                        return;
                    }

                    var fieldSymbol = c.SemanticModel.GetSymbolInfo(memberAccess.Expression).Symbol as IFieldSymbol;
                    if (!ClassWithIDisposableMembers.FieldIsRelevant(fieldSymbol))
                    {
                        return;
                    }

                    var methodSymbol = c.SemanticModel.GetSymbolInfo(invocation).Symbol as IMethodSymbol;
                    if (methodSymbol == null)
                    {
                        return;
                    }

                    var disposeMethod = (IMethodSymbol)analysisContext.Compilation.GetSpecialType(SpecialType.System_IDisposable).GetMembers("Dispose").Single();

                    if (methodSymbol.Equals(
                            methodSymbol.ContainingType.FindImplementationForInterfaceMember(disposeMethod)))
                    {
                        fieldsDisposed = fieldsDisposed.Add(fieldSymbol);
                    }

                }, SyntaxKind.InvocationExpression);

                analysisContext.RegisterCompilationEndAction(c =>
                {
                    var internallyInitializedFields = disposableFields.Intersect(fieldsAssigned);
                    var nonDisposedFields = internallyInitializedFields.Except(fieldsDisposed);

                    foreach (var nonDisposedField in nonDisposedFields)
                    {
                        var declarationReference = nonDisposedField.DeclaringSyntaxReferences.FirstOrDefault();
                        if (declarationReference == null)
                        {
                            continue;
                        }
                        var fieldSyntax = declarationReference.GetSyntax() as VariableDeclaratorSyntax;
                        if (fieldSyntax == null)
                        {
                            continue;
                        }

                        c.ReportDiagnostic(Diagnostic.Create(Rule, fieldSyntax.Identifier.GetLocation(), fieldSyntax.Identifier.ValueText));
                    }
                });
            });
        }
    }
}
