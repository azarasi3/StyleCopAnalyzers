﻿// Copyright (c) Tunnel Vision Laboratories, LLC. All Rights Reserved.
// Licensed under the Apache License, Version 2.0. See LICENSE in the project root for license information.

namespace StyleCop.Analyzers
{
    using System.Collections.Concurrent;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Threading;
    using Helpers;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;
    using Microsoft.CodeAnalysis.Diagnostics;

    internal static class GeneratedCodeAnalysisExtensions
    {
        /// <summary>
        /// Checks whether the given document is auto generated by a tool
        /// (based on filename or comment header).
        /// </summary>
        /// <remarks>
        /// <para>The exact conditions used to identify generated code are subject to change in future releases. The current algorithm uses the following checks.</para>
        /// <para>Code is considered generated if it meets any of the following conditions.</para>
        /// <list type="bullet">
        /// <item>The code is contained in a file which starts with a comment containing the text
        /// <c>&lt;auto-generated</c>.</item>
        /// <item>The code is contained in a file with a name matching certain patterns (case-insensitive):
        /// <list type="bullet">
        /// <item>*.designer.cs</item>
        /// </list>
        /// </item>
        /// </list>
        /// </remarks>
        /// <param name="tree">The syntax tree to examine.</param>
        /// <param name="cache">The concurrent results cache.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that the task will observe.</param>
        /// <returns>
        /// <para><see langword="true"/> if <paramref name="tree"/> is located in generated code; otherwise,
        /// <see langword="false"/>. If <paramref name="tree"/> is <see langword="null"/>, this method returns
        /// <see langword="false"/>.</para>
        /// </returns>
        public static bool IsGeneratedDocument(this SyntaxTree tree, ConcurrentDictionary<SyntaxTree, bool> cache, CancellationToken cancellationToken)
        {
            if (tree == null)
            {
                return false;
            }

            bool result;
            if (cache.TryGetValue(tree, out result))
            {
                return result;
            }

            bool generated = IsGeneratedDocumentNoCache(tree, cancellationToken);
            cache.TryAdd(tree, generated);
            return generated;
        }

        /// <summary>
        /// Checks whether the given node or its containing document is auto generated by a tool.
        /// </summary>
        /// <remarks>
        /// <para>This method uses <see cref="IsGeneratedDocument(SyntaxTree, ConcurrentDictionary{SyntaxTree, bool}, CancellationToken)"/> to determine which
        /// code is considered "generated".</para>
        /// </remarks>
        /// <param name="context">The analysis context for a <see cref="SyntaxNode"/>.</param>
        /// <param name="cache">The concurrent results cache.</param>
        /// <returns>
        /// <para><see langword="true"/> if the <see cref="SyntaxNode"/> contained in <paramref name="context"/> is
        /// located in generated code; otherwise, <see langword="false"/>.</para>
        /// </returns>
        internal static bool IsGenerated(this SyntaxNodeAnalysisContext context, ConcurrentDictionary<SyntaxTree, bool> cache)
        {
            return IsGeneratedDocument(context.Node.SyntaxTree, cache, context.CancellationToken);
        }

        /// <summary>
        /// Checks whether the given document is auto generated by a tool.
        /// </summary>
        /// <remarks>
        /// <para>This method uses <see cref="IsGeneratedDocument(SyntaxTree, ConcurrentDictionary{SyntaxTree, bool}, CancellationToken)"/> to determine which
        /// code is considered "generated".</para>
        /// </remarks>
        /// <param name="context">The analysis context for a <see cref="SyntaxTree"/>.</param>
        /// <param name="cache">The concurrent results cache.</param>
        /// <returns>
        /// <para><see langword="true"/> if the <see cref="SyntaxTree"/> contained in <paramref name="context"/> is
        /// located in generated code; otherwise, <see langword="false"/>.</para>
        /// </returns>
        internal static bool IsGeneratedDocument(this SyntaxTreeAnalysisContext context, ConcurrentDictionary<SyntaxTree, bool> cache)
        {
            return IsGeneratedDocument(context.Tree, cache, context.CancellationToken);
        }

        private static bool IsGeneratedDocumentNoCache(SyntaxTree tree, CancellationToken cancellationToken)
        {
            return IsGeneratedFileName(tree.FilePath)
                || HasAutoGeneratedComment(tree, cancellationToken)
                || IsEmpty(tree, cancellationToken);
        }

        /// <summary>
        /// Checks whether the given document has an auto-generated comment as its header.
        /// </summary>
        /// <param name="tree">The syntax tree to examine.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that the task will observe.</param>
        /// <returns>
        /// <para><see langword="true"/> if <paramref name="tree"/> starts with a comment containing the text
        /// <c>&lt;auto-generated</c>; otherwise, <see langword="false"/>.</para>
        /// </returns>
        private static bool HasAutoGeneratedComment(SyntaxTree tree, CancellationToken cancellationToken)
        {
            var root = tree.GetRoot(cancellationToken);
            var firstToken = root.GetFirstToken();
            SyntaxTriviaList trivia;
            if (firstToken == default(SyntaxToken))
            {
                var token = ((CompilationUnitSyntax)root).EndOfFileToken;
                if (!token.HasLeadingTrivia)
                {
                    return false;
                }

                trivia = token.LeadingTrivia;
            }
            else
            {
                if (!firstToken.HasLeadingTrivia)
                {
                    return false;
                }

                trivia = firstToken.LeadingTrivia;
            }

            var comments = trivia.Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) || t.IsKind(SyntaxKind.MultiLineCommentTrivia));
            return comments.Any(t =>
            {
                string s = t.ToString();
                return s.Contains("<auto-generated") || s.Contains("<autogenerated");
            });
        }

        /// <summary>
        /// Checks whether the given document has a filename that indicates it is a generated file.
        /// </summary>
        /// <param name="filePath">The source file name, without any path.</param>
        /// <returns>
        /// <para><see langword="true"/> if <paramref name="filePath"/> is the name of a generated file; otherwise,
        /// <see langword="false"/>.</para>
        /// </returns>
        /// <seealso cref="IsGeneratedDocument(SyntaxTree, ConcurrentDictionary{SyntaxTree, bool}, CancellationToken)"/>
        private static bool IsGeneratedFileName(string filePath)
        {
            return Regex.IsMatch(
                Path.GetFileName(filePath),
                @"\.designer\.cs$",
                RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture);
        }

        /// <summary>
        /// Checks if a given <see cref="SyntaxTree"/> only contains whitespaces. We don't want to analyze empty files.
        /// </summary>
        /// <param name="tree">The syntax tree to examine.</param>
        /// <param name="cancellationToken">The <see cref="CancellationToken"/> that the task will observe.</param>
        /// <returns>
        /// <para><see langword="true"/> if <paramref name="tree"/> only contains whitespaces; otherwise, <see langword="false"/>.</para>
        /// </returns>
        private static bool IsEmpty(SyntaxTree tree, CancellationToken cancellationToken)
        {
            var root = tree.GetRoot(cancellationToken);
            var firstToken = root.GetFirstToken(includeZeroWidth: true);

            return firstToken.IsKind(SyntaxKind.EndOfFileToken)
                && TriviaHelper.IndexOfFirstNonWhitespaceTrivia(firstToken.LeadingTrivia) == -1;
        }
    }
}
