## 1.8.1 | 2026-11-05
### Correções
- Os links da tela inicial (Sites compatíveis, Como copiar o link e Mostrar sugestões) voltaram a responder ao clique.

## 1.8.0 | 2026-11-05
### Novidades
- Fotos do X (Twitter), do Instagram e de outros sites, incluindo carrosséis do Instagram com todas as imagens e vídeos.
- Links de playlist perguntam o que baixar: a playlist inteira, com uma linha por vídeo, ou só um vídeo escolhido na lista.
- Em links de vídeo dentro de uma playlist, o vídeo do link já vem marcado.
- Sugestões de vídeos podem ser removidas uma a uma ou ocultadas de vez.
### Melhorias
- Sugestão baixada ou removida sai da tela e não volta na mesma sessão.
- Com as sugestões ocultas, a tela inicial mostra o link para trazê-las de volta.

## 1.7.0 | 2026-11-03
### Novidades
- Músicas do YouTube Music e do SoundCloud, baixadas do próprio site.
- Músicas, álbuns e playlists do Spotify e do Deezer, e músicas e álbuns da Apple Music. Esses serviços protegem o áudio, então o programa encontra a mesma música no YouTube Music e baixa de lá.
- Álbuns e playlists entram na fila com uma linha por música.
- Links de música baixam só o áudio, em MP3, com artista e título no nome do arquivo.
- Escolha do navegador que abre os links, na barra lateral. Os navegadores abertos aparecem primeiro, e por padrão o programa pergunta antes de abrir.
- Login com Brave, Opera e Vivaldi em Conteúdo restrito.
### Melhorias
- Sites compatíveis mais claros: lista na tela inicial e tópico novo na Ajuda, dizendo de onde vem cada arquivo.
- O menu de Conteúdo restrito mostra só os navegadores instalados e marca os que estão abertos.
- Cada linha mostra o serviço de origem, como “Spotify · via YouTube Music”.

## 1.6.1 | 2026-10-31
### Correções
- Vídeos diferentes com o mesmo título não se sobrescrevem mais: o segundo é salvo com o identificador no nome.
- Pastas com %, $ ou raiz de unidade (como D:\) agora recebem o arquivo no lugar certo.
- Atualização travada do motor de download não bloqueia mais a fila.
- O motor de download é restaurado sozinho se estiver danificado.
- Uma linha corrompida não apaga mais a Biblioteca, e opções e Biblioteca são gravadas de forma segura.
- Depois de pausar e continuar, o download mostra “Salvo” em vez de “Já estava na pasta”.
### Segurança
- Dados vindos de sites não podem mais virar opções do motor de download.
- Sugestões não usam mais o login do navegador.
- Relatórios e registros removem mais tipos de dados sensíveis.
- Miniaturas têm limite de tempo, tamanho e dimensões.
- Programas e bibliotecas deixados na pasta do Downloader não são mais carregados no lugar dos do Windows.
### Melhorias
- Biblioteca com muitos itens abre rápido e usa muito menos memória.
- Colar milhares de links não trava a janela.
- Ao fechar ou cair, o programa não deixa processos em segundo plano.

## 1.6.0 | 2026-10-31
### Novidades
- Termos e políticas: Termos de Uso, Política de Privacidade, Diretrizes de Uso, Código de Ética e autoria, na barra lateral.
- Aviso sobre direitos autorais na tela de Downloads.
### Melhorias
- Ajuda reorganizada por assunto, com um tópico por vez.
- Novidades no mesmo formato, com uma versão por vez.
- Ajuda e Novidades menores na barra lateral.
- Pasta de destino mais clara, com o caminho e o botão Alterar na mesma área.

## 1.5.0 | 2026-10-31
### Novidades
- Tamanho antes de baixar: ao colar um link, aparecem o título, a miniatura, a duração e quanto o arquivo vai ocupar com as opções escolhidas.
- Modo noite: escolha entre tema do Windows, claro ou escuro, na barra lateral.
- Títulos de vídeo agora são links: clique para abrir o vídeo no navegador.
### Melhorias
- Interface nova: barra lateral, ícones, menos texto e janela maior, que usa toda a largura disponível.
- A janela lembra o tamanho da última vez e se adapta quando fica estreita.
- Download concluído mostra o tamanho real do arquivo.

## 1.4.0 | 2026-10-31
### Novidades
- Baixar só um trecho: escolha o início e o fim do vídeo. Só o trecho é baixado, não o vídeo inteiro.
- Área de Ajuda com o passo a passo de cada plataforma.
- Área de Novidades, esta aqui.
- Opção de usar um arquivo de cookies para conteúdo restrito.
- Relatório de erro: copie ou salve os detalhes para enviar ao suporte.
### Melhorias
- Interface reorganizada: link reconhecido enquanto você digita, e cada download mostra a etapa real em que está.
- Erros agora têm código, explicação, o que fazer e um botão de recuperação.
- Um erro não apaga mais o link: dá para corrigir e tentar de novo.
- Cancelar agora mostra o estado “Cancelado”, com opção de baixar de novo.
- O botão de pausar só aparece quando a pausa é possível.
### Correções
- Vídeos públicos falhavam quando um navegador estava selecionado. Agora o login só é usado se o site pedir.
- Cancelar deixava arquivos temporários no PC. Agora eles são apagados.
- “Baixar de novo” em um vídeo já salvo não baixava de novo.
### Compatibilidade
- Chrome e Edge protegem o login salvo no Windows, e essa leitura pode ser bloqueada. Firefox e arquivo de cookies são as alternativas.

## 1.3.0 | 2026-10-30
### Novidades
- Programa em um único arquivo, com instalação, atualização e desinstalação sem precisar de administrador.
- Botão Sobre, com as licenças dos componentes.
### Melhorias
- Componentes de download verificados e restaurados automaticamente se forem alterados.
- O programa avisa quando já está aberto.
### Compatibilidade
- Passou a usar uma versão menor do FFmpeg, com licença LGPL.
- Removido o modo específico da rede local.

## 1.2.0 | 2026-10-28
### Novidades
- Aba Biblioteca: tudo que você baixou, com busca, abrir, mostrar na pasta e baixar de novo.
- Sugestões de vídeos dos canais que você mais baixa.
- Na primeira vez, o programa pergunta onde salvar.
### Correções
- Títulos com acento apareciam quebrados.
- Miniaturas não apareciam.

## 1.1.0 | 2026-10-28
### Novidades
- Primeira versão numerada: fila de downloads de YouTube, Instagram, TikTok, X e outros sites.
- Tipos: vídeo, vídeo e áudio separados, ou só áudio, com escolha de qualidade e formato.
- Pausar e retomar downloads.
