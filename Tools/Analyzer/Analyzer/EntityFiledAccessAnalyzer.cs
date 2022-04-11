﻿using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace ET.Analyzer
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class EntityFiledAccessAnalyzer : DiagnosticAnalyzer
    {
        private const string Title = "实体字段访问错误";
        
        private const string MessageFormat = "实体: {0} 字段: {1} 只能在实体类生命周期组件或友元类(含有FriendClassAttribute)中访问";
        
        private const string Description = "请使用实体类属性或方法访问其他实体字段.";
        
        private const string EntityType = "ET.Entity";
        
        private const string ObjectSystemAttribute = "ET.ObjectSystemAttribute";
        
        private const string ISystemType = "ET.ISystemType";

        private const string FriendClassAttribute = "ET.FriendClassAttribute";

        private static readonly DiagnosticDescriptor Rule = new DiagnosticDescriptor(DiagnosticIds.EntityFiledAccessAnalyzerRuleId,
            Title,
            MessageFormat,
            DiagnosticCategories.Hotfix,
            DiagnosticSeverity.Error, true, Description);
        
        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => ImmutableArray.Create(Rule);
        
        
        public override void Initialize(AnalysisContext context)
        {
            if (!AnalyzerGlobalSetting.EnableAnalyzer)
            {
                return;
            }
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();
            context.RegisterSyntaxNodeAction(this.AnalyzeMemberAccessExpression, SyntaxKind.SimpleMemberAccessExpression);
        }

        private void AnalyzeMemberAccessExpression(SyntaxNodeAnalysisContext context)
        {
            if (!AnalyzerHelper.IsAssemblyNeedAnalyze(context.Compilation.AssemblyName,AnalyzeAssembly.AllHotfix))
            {
                return;
            }
            
            if (!(context.Node is MemberAccessExpressionSyntax memberAccessExpressionSyntax))
            {
                return;
            }
            
            // -----筛选出实体类的字段symbol-----
            
            var filedSymbol = context.SemanticModel.GetSymbolInfo(memberAccessExpressionSyntax).Symbol;
            if (filedSymbol ==null || !(filedSymbol is IFieldSymbol))
            {
                return;
            }

            if (filedSymbol.IsStatic)
            {
                return;
            }

            if (filedSymbol.ContainingType.BaseType?.ToString()!= EntityType)
            {
                return;
            }
            
            // -----筛选出在实体类和实体System外部字段访问-----
            // 实体System包括awakeSystem updateSystem等生命周期类和 componentSystem静态方法类

            var accessFieldClassDeclaretion = memberAccessExpressionSyntax.GetParentClassDeclaration();
            if (accessFieldClassDeclaretion==null)
            {
                return;
            }
            var accessFieldClassSymbol = context.SemanticModel.GetDeclaredSymbol(accessFieldClassDeclaretion);

            if (accessFieldClassSymbol == null)
            {
                return;
            }

            // 实体基类忽略处理
            if (accessFieldClassSymbol.ToString() == EntityType)
            {
                return;
            }
            
            //判断是否在实体类生命周期System中
            if(CheckIsEntityLifecycleSystem(accessFieldClassSymbol, filedSymbol.ContainingType))
            {
                return;
            }

            //判断是否在实体类的友元类中
            if (CheckIsEntityFriendClass(accessFieldClassSymbol,filedSymbol.ContainingType))
            {
                return;
            }
            
            Diagnostic diagnostic =Diagnostic.Create(Rule,  memberAccessExpressionSyntax.GetLocation(), filedSymbol.ContainingType.Name, filedSymbol.Name);
            context.ReportDiagnostic(diagnostic);
        }
        
        private bool CheckIsEntityLifecycleSystem(INamedTypeSymbol accessFieldClassSymbol, INamedTypeSymbol entityTypeSymbol)
        {
            if (accessFieldClassSymbol.BaseType==null ||!accessFieldClassSymbol.BaseType.IsGenericType)
            {
                return false;
            }
            // 判断是否含有 ObjectSystem Attribute 且继承了接口 ISystemType
            if (accessFieldClassSymbol.BaseType.HasAttribute(ObjectSystemAttribute) && accessFieldClassSymbol.HasInterface(ISystemType))
            {
                // 获取 accessFieldClassSymbol 父类的实体类型参数
                var entityTypeArgumentSymbol = accessFieldClassSymbol.BaseType.TypeArguments.FirstOrDefault();
                if (entityTypeArgumentSymbol == null)
                {
                    return false;
                }
                 // 判断 accessFieldClassSymbol 父类的实体类型参数是否为 entityTypeSymbol
                 if (entityTypeArgumentSymbol.ToString() == entityTypeSymbol.ToString())
                 {
                     return true;
                 }
            }
            return false;
        }

        private bool CheckIsEntityFriendClass(INamedTypeSymbol accessFieldTypeSymbol, INamedTypeSymbol entityTypeSymbol)
        {
            var attributes = accessFieldTypeSymbol.GetAttributes();
            foreach (var attributeData in attributes)
            {
                if (attributeData.AttributeClass?.ToString() != FriendClassAttribute)
                {
                    continue;
                }

                if (attributeData.ConstructorArguments[0].Value is INamedTypeSymbol namedTypeSymbol==false)
                {
                    continue;
                }

                if (namedTypeSymbol.ToString() == entityTypeSymbol.ToString())
                {
                    return true;
                }
            }

            return false;
        }
    }
}