using System.Collections.Immutable;

public enum TokenType { Semicolon, Assignment, LeftParenthesis, RightParenthesis, Add, Subtract, Multiply, Divide, Id, Integer, Whitespace }
public record struct Token(int Start, ReadOnlyMemory<char> Memory, TokenType Type);

public record Node();
public record Program(Statements Statements, Expression Eval) : Node();
public record Statement() : Node();
public record Statements(ImmutableList<Statement> StatementList) : Node();
public record AssignmentStatement(Token Identifier, Expression Expression) : Statement();
public record Expression() : Node();
public record NameExpression(Token Identifier) : Expression();
public record BinaryExpression(Expression Left, Token Operator, Expression Right) : Expression();
public record IntLiteralExpression(int Value) : Expression();