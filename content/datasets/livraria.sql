-- Dataset "livraria" dos exercícios de SQL (módulo "Primeiros passos com SQL").
-- Fonte: roteiro do PO na issue #22. Verificado no PostgreSQL 16.
-- Conceição Evaristo fica sem livros de propósito: prepara a aula de LEFT JOIN.
DROP TABLE IF EXISTS livros; DROP TABLE IF EXISTS autores;
CREATE TABLE autores (
  id    integer PRIMARY KEY,
  nome  text    NOT NULL,
  pais  text    NOT NULL
);
CREATE TABLE livros (
  id              integer       PRIMARY KEY,
  titulo          text          NOT NULL,
  autor_id        integer       NOT NULL REFERENCES autores(id),
  genero          text          NOT NULL,
  ano_publicacao  integer       NOT NULL,
  preco           numeric(10,2) NOT NULL,
  estoque         integer       NOT NULL
);
INSERT INTO autores (id, nome, pais) VALUES
 (1, 'Machado de Assis',       'Brasil'),
 (2, 'Clarice Lispector',      'Brasil'),
 (3, 'J.R.R. Tolkien',         'Reino Unido'),
 (4, 'Agatha Christie',        'Reino Unido'),
 (5, 'Gabriel García Márquez', 'Colômbia'),
 (6, 'Conceição Evaristo',     'Brasil');
INSERT INTO livros (id, titulo, autor_id, genero, ano_publicacao, preco, estoque) VALUES
 (1,  'Dom Casmurro',                            1, 'Romance',  1899, 29.90, 12),
 (2,  'Memórias Póstumas de Brás Cubas',         1, 'Romance',  1881, 34.90,  5),
 (3,  'A Hora da Estrela',                       2, 'Romance',  1977, 39.90,  0),
 (4,  'O Hobbit',                                3, 'Fantasia', 1937, 49.90,  8),
 (5,  'O Senhor dos Anéis: A Sociedade do Anel', 3, 'Fantasia', 1954, 69.90,  3),
 (6,  'Assassinato no Expresso do Oriente',      4, 'Mistério', 1934, 32.50,  7),
 (7,  'E Não Sobrou Nenhum',                     4, 'Mistério', 1939, 36.00,  0),
 (8,  'Cem Anos de Solidão',                     5, 'Romance',  1967, 54.90,  4),
 (9,  'O Silmarillion',                          3, 'Fantasia', 1977, 59.90,  2),
 (10, 'Laços de Família',                        2, 'Contos',   1960, 27.90,  9);
