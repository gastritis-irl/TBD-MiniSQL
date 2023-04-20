parser grammar abkr_grammar;

options { tokenVocab=abkr_grammarLexer; }

statement: create_database_statement | create_table_statement | drop_database_statement | drop_table_statement | create_index_statement | drop_index_statement | insert_statement | delete_statement;

create_database_statement: CREATE DATABASE identifier;

create_table_statement: CREATE TABLE identifier DOT identifier LPAREN column_definition_list RPAREN;

drop_database_statement: DROP DATABASE identifier;

drop_table_statement: DROP TABLE identifier DOT identifier;

create_index_statement: CREATE (UNIQUE)? INDEX identifier ON identifier LPAREN identifier_list RPAREN;

drop_index_statement: DROP INDEX identifier ON identifier;

column_definition_list: column_definition (COMMA column_definition)*;

column_definition: identifier data_type column_constraint*;

column_constraint
    : PRIMARY KEY
    | UNIQUE
    | FOREIGN KEY REFERENCES identifier DOT identifier
    ;

data_type: INT | VARCHAR LPAREN NUMBER RPAREN;

identifier_list: identifier (COMMA identifier)*;

identifier: IDENTIFIER;

insert_statement: INSERT INTO identifier DOT identifier ( LPAREN identifier_list RPAREN )? VALUES LPAREN value_list RPAREN;

delete_statement: DELETE FROM identifier DOT identifier WHERE identifier EQUALS value;

value_list: value (COMMA value)*;

value: STRING | NUMBER;
